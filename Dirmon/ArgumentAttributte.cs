using System;
using System.Runtime.CompilerServices;

namespace Dirmon
{
    /// <summary>
    ///     A decorator for program argument types
    /// </summary>
    public class ArgumentAttribute : Attribute
    {
        private const string LongNamePrefix = "--";

        private const string ShortNamePrefix = "-";

        /// <summary>
        ///     Specify the string that identifies this argument excluding
        ///     the prefix.
        /// </summary>
        /// <example>
        ///    app.exe --input
        ///    ...
        ///     [ArgumentAttribute("input")]
        /// </example>
        /// <param name="longName">Name portion of argument</param>
        /// <param name="propertyName">Property name this is attached to</param>
        public ArgumentAttribute(string longName, [CallerMemberName] string propertyName = null)
        {
            LongName = longName;
            PropertyName = propertyName;
        }

        /// <summary>
        ///     Name of the property this is attached to
        /// </summary>
        public string PropertyName { get; }
        
        /// <summary>
        ///     The long name of the option is preceded by
        ///     LongNamePrefix and uses a single '-' on
        ///     word boundaries
        /// </summary>
        private string LongName { get; }

        /// <summary>
        ///     The short name of the option is preceded by 
        ///     ShortNamePrefix and should be a single character
        /// </summary>
        public char ShortName { get; set; }

        /// <summary>
        ///     Describes what this argument provides
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        ///     Example invocation of this argument
        /// </summary>
        public string Example { get; set; }

        /// <summary>
        ///     If true, this is a required argument
        /// </summary>
        public bool Required { get; set; }
        
        /// <summary>
        ///     If true, this is a flag argument (no argument to this parameter)
        /// </summary>
        public bool IsFlag { get; set; }

        /// <summary>
        ///    Returns LongName|ShortName pair, in brackets if optional
        /// </summary>
        public string Usage =>
            $"{(Required ? "" : "[")}{LongNamePrefix}{LongName}|{ShortNamePrefix}{ShortName}{(Required ? "" : "]")}";

        /// <summary>
        ///    Returns true if the raw string value matches either of the long or short specifiers
        ///    for this attribute.
        /// </summary>
        /// <param name="rawValue">Raws parameter input</param>
        /// <returns>True if input indicates this is the desired attribute</returns>
        public bool IsMatch(string rawValue)
        {
            var noPrefix = rawValue.Replace(LongNamePrefix, "").Replace(ShortNamePrefix, "");
            if (noPrefix == LongName)
            {
                return true;
            }

            if (noPrefix.Length == 1 && noPrefix.ToCharArray()[0] == ShortName)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Returns long and short name followed by usage string
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return $"{LongNamePrefix}{LongName},{ShortNamePrefix}{ShortName}\t{Description}";
        }
    }
}