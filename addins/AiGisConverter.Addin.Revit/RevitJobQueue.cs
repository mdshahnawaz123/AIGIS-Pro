using System;
using System.Collections.Concurrent;
using System.Threading;
using Autodesk.Revit.UI;

namespace AiGisConverter.Addin.Revit
{
    /// <summary>
    /// Runs work on Revit's own thread and hands the result back to a background caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Revit API may only be touched from Revit's thread, and only while Revit is idle. The
    /// bridge server, by contrast, answers on a thread pool thread. Everything that reads a
    /// document therefore has to cross that boundary, which is what an
    /// <see cref="ExternalEvent"/> is for: raising one asks Revit to call
    /// <see cref="Execute"/> the next time it is idle.
    /// </para>
    /// <para>
    /// The caller then blocks. That is deliberate. The bridge contract is one request, one reply on
    /// the same connection, so there is nothing useful for the calling thread to do until Revit has
    /// answered, and returning early would mean inventing a correlation scheme the protocol does
    /// not have.
    /// </para>
    /// <para>
    /// The wait is bounded. Revit is not idle while a modal dialog is open, and a user who leaves
    /// one on screen would otherwise hold the bridge thread indefinitely; a timeout turns that into
    /// a message naming the cause.
    /// </para>
    /// </remarks>
    internal sealed class RevitJobQueue : IExternalEventHandler, IDisposable
    {
        private readonly ConcurrentQueue<Job> _jobs = new ConcurrentQueue<Job>();
        private ExternalEvent _event;
        private bool _disposed;

        /// <summary>Creates the external event. Must be called from Revit's API context.</summary>
        /// <remarks><c>ExternalEvent.Create</c> is itself a Revit API call, so this belongs in start-up.</remarks>
        internal void Initialise()
        {
            _event = ExternalEvent.Create(this);
        }

        /// <summary>Gets a value indicating whether the queue can accept work.</summary>
        internal bool IsReady
        {
            get { return _event != null && !_disposed; }
        }

        /// <summary>Runs <paramref name="work"/> on Revit's thread and returns its result.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="work">The work to run. Receives the live <see cref="UIApplication"/>.</param>
        /// <param name="timeout">How long to wait for Revit to become idle and finish.</param>
        /// <returns>The value produced by <paramref name="work"/>.</returns>
        internal T Run<T>(Func<UIApplication, T> work, TimeSpan timeout)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            if (!IsReady)
            {
                throw new InvalidOperationException(
                    "The add-in's Revit event has not been created, so no work can be marshalled onto Revit's thread.");
            }

            Job job = new Job(application => work(application));

            _jobs.Enqueue(job);
            _event.Raise();

            if (!job.Wait(timeout))
            {
                // Abandoned rather than removed: it may already be mid-flight on Revit's thread,
                // and the flag stops it writing to state this caller has stopped waiting on.
                // Deliberately not disposed - Revit may still be inside it, and disposing the
                // handle it is about to signal would turn a slow read into a crash in Revit.
                job.Abandon();

                throw new TimeoutException(
                    "Revit did not become idle within "
                    + ((int)timeout.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " seconds. A modal dialog or a long-running command in Revit will do this; "
                    + "close it and retry.");
            }

            try
            {
                if (job.Failure != null)
                {
                    throw new InvalidOperationException(job.Failure.Message, job.Failure);
                }

                return (T)job.Result;
            }
            finally
            {
                job.Dispose();
            }
        }

        /// <inheritdoc />
        public void Execute(UIApplication app)
        {
            Job job;

            while (_jobs.TryDequeue(out job))
            {
                job.Run(app);
            }
        }

        /// <inheritdoc />
        public string GetName()
        {
            return "AI GIS Converter bridge";
        }

        /// <summary>Releases the external event.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_event != null)
            {
                _event.Dispose();
                _event = null;
            }

            Job pending;

            while (_jobs.TryDequeue(out pending))
            {
                pending.Abandon();
            }

            GC.SuppressFinalize(this);
        }

        private sealed class Job : IDisposable
        {
            private readonly Func<UIApplication, object> _work;
            private readonly ManualResetEventSlim _completed = new ManualResetEventSlim(false);
            private int _abandoned;

            internal Job(Func<UIApplication, object> work)
            {
                _work = work;
            }

            internal object Result { get; private set; }

            internal Exception Failure { get; private set; }

            internal void Abandon()
            {
                Interlocked.Exchange(ref _abandoned, 1);
            }

            internal void Run(UIApplication application)
            {
                if (Volatile.Read(ref _abandoned) == 1)
                {
                    return;
                }

                try
                {
                    Result = _work(application);
                }
                catch (Exception exception)
                {
                    // Carried back to the caller rather than thrown here: an exception escaping an
                    // IExternalEventHandler surfaces to the user as a Revit crash dialog about an
                    // add-in they did not knowingly invoke.
                    Failure = exception;
                }
                finally
                {
                    if (Volatile.Read(ref _abandoned) == 0)
                    {
                        _completed.Set();
                    }
                }
            }

            /// <summary>Waits for the job to finish on Revit's thread.</summary>
            internal bool Wait(TimeSpan timeout)
            {
                return _completed.Wait(timeout);
            }

            /// <summary>Releases the completion handle.</summary>
            public void Dispose()
            {
                _completed.Dispose();

                GC.SuppressFinalize(this);
            }
        }
    }
}
