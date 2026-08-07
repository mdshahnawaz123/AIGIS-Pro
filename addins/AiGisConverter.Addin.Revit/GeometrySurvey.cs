using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Autodesk.Revit.DB;

namespace AiGisConverter.Addin.Revit
{
    /// <summary>
    /// Records what a geometry traversal actually encountered.
    /// </summary>
    /// <remarks>
    /// "No vertices were collected" is not a diagnosis. An element whose geometry holds nothing, one
    /// holding solids with no faces, and one holding only curves are three different problems with
    /// three different fixes, and they are indistinguishable from the absence of a result. This
    /// counts what was met on the way so the report can tell them apart.
    /// </remarks>
    internal sealed class GeometrySurvey
    {
        private readonly Dictionary<string, int> _kinds = new Dictionary<string, int>();

        /// <summary>Gets or sets a value indicating whether any surface geometry was met.</summary>
        /// <remarks>
        /// Separates an element that has a shape from one that is only a centreline. Both produce
        /// vertices, and by the time a footprint is being built the two are indistinguishable - but
        /// the hull of a pipe run is a polygon enclosing the pipe, which is not the pipe.
        /// </remarks>
        internal bool SawSurface { get; set; }

        /// <summary>Gets or sets how many solids carried no faces at all.</summary>
        internal int EmptySolids { get; set; }

        /// <summary>Gets or sets how many faces were seen across every solid.</summary>
        internal int SolidFaces { get; set; }

        /// <summary>Records one geometry object met during traversal.</summary>
        /// <param name="item">The geometry object.</param>
        internal void Seen(GeometryObject item)
        {
            if (item == null)
            {
                return;
            }

            string kind = item.GetType().Name;
            int count;

            _kinds[kind] = _kinds.TryGetValue(kind, out count) ? count + 1 : 1;
        }

        /// <summary>Describes the traversal in one line, for the failure report.</summary>
        /// <returns>The description.</returns>
        internal string Describe()
        {
            StringBuilder text = new StringBuilder();

            text.Append("objects=");

            if (_kinds.Count == 0)
            {
                text.Append("none");
            }
            else
            {
                bool first = true;

                foreach (KeyValuePair<string, int> pair in _kinds)
                {
                    if (!first)
                    {
                        text.Append('+');
                    }

                    text.Append(pair.Key).Append(':').Append(pair.Value.ToString(CultureInfo.InvariantCulture));
                    first = false;
                }
            }

            text.Append(", surface=").Append(SawSurface ? "yes" : "no");
            text.Append(", emptySolids=").Append(EmptySolids.ToString(CultureInfo.InvariantCulture));
            text.Append(", faces=").Append(SolidFaces.ToString(CultureInfo.InvariantCulture));

            return text.ToString();
        }
    }
}
