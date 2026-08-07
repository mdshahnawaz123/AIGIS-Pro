using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AiGisConverter.Addin.Revit
{
    /// <summary>
    /// Attribute key and value rules for exported BIM parameters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Free of Revit types, for the same reason <see cref="Footprint"/> is: these rules decide what
    /// every downstream consumer sees, and rules that can only be exercised inside Revit are rules
    /// nobody checks.
    /// </para>
    /// <para>
    /// Parameter names come from the model author. They can collide with the fields the reader
    /// writes itself, collide with each other across the instance and type of one element, contain
    /// anything a keyboard produces, and run to any length. Prefixes settle the first two by
    /// construction rather than by a list of reserved words that would need extending every time a
    /// field is added.
    /// </para>
    /// </remarks>
    internal static class BimNaming
    {
        /// <summary>Prefix marking a key as an instance parameter read from the model.</summary>
        internal const string InstancePrefix = "p_";

        /// <summary>Prefix marking a key as a type parameter read from the model.</summary>
        internal const string TypePrefix = "tp_";

        /// <summary>The longest value written for a single parameter.</summary>
        /// <remarks>
        /// A parameter holding a paragraph of specification text is legal and occasionally real.
        /// Truncation is marked so a reader can tell a shortened value from a complete one.
        /// </remarks>
        internal const int MaximumValueLength = 512;

        /// <summary>Appended to a value that was shortened.</summary>
        internal const string TruncationMarker = "...";

        /// <summary>Turns a parameter name into a key that survives every export format.</summary>
        /// <remarks>
        /// Letters and digits are kept in any script. The model that prompted this work is authored
        /// in Chinese, and reducing its parameter names to ASCII would replace them with rows of
        /// underscores - technically valid keys that name nothing.
        /// </remarks>
        /// <param name="name">The parameter name as the model holds it.</param>
        /// <returns>The sanitised key, or null when nothing usable remains.</returns>
        internal static string Sanitise(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            StringBuilder builder = new StringBuilder(name.Length);
            bool lastWasUnderscore = false;

            foreach (char character in name)
            {
                if (char.IsLetterOrDigit(character) || character == '_')
                {
                    builder.Append(character);
                    lastWasUnderscore = character == '_';
                    continue;
                }

                if (!lastWasUnderscore && builder.Length > 0)
                {
                    builder.Append('_');
                    lastWasUnderscore = true;
                }
            }

            string key = builder.ToString().Trim('_');

            return key.Length == 0 ? null : key;
        }

        /// <summary>Builds the key for an instance parameter.</summary>
        /// <param name="name">The parameter name.</param>
        /// <returns>The key, or null when the name yields nothing usable.</returns>
        internal static string InstanceKey(string name)
        {
            string sanitised = Sanitise(name);

            return sanitised == null ? null : InstancePrefix + sanitised;
        }

        /// <summary>Builds the key for a type parameter.</summary>
        /// <param name="name">The parameter name.</param>
        /// <returns>The key, or null when the name yields nothing usable.</returns>
        internal static string TypeKey(string name)
        {
            string sanitised = Sanitise(name);

            return sanitised == null ? null : TypePrefix + sanitised;
        }

        /// <summary>Shortens a value that would otherwise dominate the export.</summary>
        /// <param name="value">The value to bound.</param>
        /// <returns>The value, shortened and marked if it was too long.</returns>
        internal static string Truncate(string value)
        {
            if (value == null || value.Length <= MaximumValueLength)
            {
                return value;
            }

            return value.Substring(0, MaximumValueLength - TruncationMarker.Length) + TruncationMarker;
        }

        /// <summary>Formats a number for the wire.</summary>
        /// <remarks>
        /// Round-trip precision and invariant culture. The value is parsed by a process with its own
        /// locale, where a comma decimal separator becomes a thousands separator and the number
        /// silently changes by three orders of magnitude.
        /// </remarks>
        /// <param name="value">The number.</param>
        /// <returns>The formatted number, or null when it is not finite.</returns>
        internal static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                // A non-finite number is not a measurement. Writing "NaN" would put a string into a
                // column every consumer has inferred as numeric.
                return null;
            }

            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Formats an integer for the wire.</summary>
        /// <param name="value">The integer.</param>
        /// <returns>The formatted integer.</returns>
        internal static string Integer(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Formats Revit's integer-backed yes/no as a boolean.</summary>
        /// <param name="value">The stored integer.</param>
        /// <returns><c>true</c> or <c>false</c>.</returns>
        internal static string YesNo(int value)
        {
            // Lower case, matching JSON's own literals, so a consumer parsing loosely gets a
            // boolean rather than a string that happens to read like one.
            return value != 0 ? "true" : "false";
        }

        /// <summary>Joins several values into one attribute.</summary>
        /// <remarks>
        /// A semicolon, because a comma appears inside Revit names often enough to matter and would
        /// make the joined value ambiguous to split.
        /// </remarks>
        /// <param name="values">The values to join. Blanks are dropped.</param>
        /// <returns>The joined value, or null when nothing remains.</returns>
        internal static string Join(IEnumerable<string> values)
        {
            if (values == null)
            {
                return null;
            }

            StringBuilder builder = new StringBuilder();

            foreach (string value in values)
            {
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }

                builder.Append(value);
            }

            return builder.Length == 0 ? null : builder.ToString();
        }
    }
}
