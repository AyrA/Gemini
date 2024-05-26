using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Gemini.Lib.Data
{
    /// <summary>
    /// Represents a gemini form
    /// </summary>
    public class Form : ICollection<FormField>, IValidateabe
    {
        /// <summary>
        /// Content type to be used for forms
        /// </summary>
        public const string ContentType = "text/gemini+form; charset=utf-8";

        /// <summary>
        /// Preamble that is added above a form
        /// </summary>
        public const string Preamble = "# This is a gemini+ form. If you see this text it means you don't have a gemini+ compatible client.";

        private readonly List<FormField> _fields;

        /// <summary>
        /// Gets the form field at the specified index
        /// </summary>
        /// <param name="index">Field index</param>
        /// <returns>Form field</returns>
        public FormField this[int index] => _fields[index];

        /// <summary>
        /// Gets the number of fields
        /// </summary>
        public int Count => _fields.Count;

        /// <summary>
        /// Gets "false"
        /// </summary>
        public bool IsReadOnly => false;

        /// <summary>
        /// URL where the form is to be submit to
        /// </summary>
        /// <remarks>If this is unset, the form is submit to the current URL</remarks>
        public Uri? Target { get; set; }

        /// <summary>
        /// Gets or sets the maximum size for the form.
        /// This is the size in bytes for the URL encoded form
        /// plus the size of body data in case of file elements.
        /// A value of <see cref="ulong.MinValue"/> or <see cref="ulong.MaxValue"/> is interpreted as "no limit"
        /// </summary>
        public ulong MaxSize { get; set; } = ulong.MinValue;

        /// <summary>
        /// Creates an empty form
        /// </summary>
        public Form()
        {
            _fields = [];
        }

        /// <summary>
        /// Creates a form from existing field data
        /// </summary>
        /// <param name="fields">Field data</param>
        public Form(IEnumerable<FormField> fields)
        {
            if (fields == null)
            {
                _fields = [];
            }
            else
            {
                _fields = fields.ToList();
            }
            Validate();
        }

        /// <summary>
        /// Validates this instance
        /// </summary>
        public void Validate()
        {
            if (Target != null && !Target.IsGeminiUrl())
            {
                throw new Exception("Target URL for the form is invalid");
            }
            if (_fields.Count == 0)
            {
                return;
            }


            if (_fields.Contains(null!))
            {
                throw new Exception("Fields contains null value");
            }
            _fields.ForEach(m => m.Validate());
        }

        /// <summary>
        /// Adds a field
        /// </summary>
        /// <param name="item"></param>
        public void Add(FormField item)
        {
            _fields.Add(item);
        }

        /// <summary>
        /// Clears this form
        /// </summary>
        public void Clear()
        {
            _fields.Clear();
        }

        /// <summary>
        /// Checks if an entry is contained in the form
        /// </summary>
        /// <param name="item">Entry</param>
        /// <returns>true, if contained</returns>
        public bool Contains(FormField item)
        {
            return _fields.Contains(item);
        }

        /// <summary>
        /// Copies all entries to the given array
        /// </summary>
        /// <param name="array">Array</param>
        /// <param name="arrayIndex">start index</param>
        public void CopyTo(FormField[] array, int arrayIndex)
        {
            _fields.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Gets the field enumerator for this form
        /// </summary>
        /// <returns>Field enumerator</returns>
        public IEnumerator<FormField> GetEnumerator()
        {
            return _fields.GetEnumerator();
        }

        /// <summary>
        /// Removes a field from the form collection
        /// </summary>
        /// <param name="item">Field</param>
        /// <returns>true, if field existed</returns>
        public bool Remove(FormField item)
        {
            return _fields.Remove(item);
        }

        /// <summary>
        /// Gets the field enumerator for this form
        /// </summary>
        /// <returns>Field enumerator</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_fields).GetEnumerator();
        }

        /// <summary>
        /// Converts this instance to a gemini response
        /// </summary>
        /// <returns>Gemini response</returns>
        public GeminiResponse ToResponse()
        {
            return GeminiResponse.Ok(Serialize(), ContentType);
        }

        /// <summary>
        /// Serializes the form into a gemini+form string
        /// </summary>
        /// <returns></returns>
        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Preamble).AppendLine();

            var extra = new List<string>();
            if (MaxSize != ulong.MinValue && MaxSize != ulong.MaxValue)
            {
                extra.Add("maxsize=" + MaxSize);
            }
            if (Target != null)
            {
                extra.Add("target=" + Uri.EscapeDataString(Target.ToString()));
            }
            if (extra.Count > 0)
            {
                sb.AppendLine("[]");
                extra.ForEach(m => sb.AppendLine(m));
            }
            foreach (var field in _fields)
            {
                sb.AppendLine(field.Serialize());
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Represents a single form field
    /// </summary>
    public class FormField : IValidateabe
    {
        private static readonly FieldType[] requiredOptions =
        [
            FieldType.Checkbox, FieldType.Select, FieldType.Radio
        ];

        /// <summary>
        /// The Name of the field.
        /// </summary>
        /// <remarks>
        /// The name in forms does not need to be unique, but it's generally recommended.
        /// </remarks>
        public string? Name { get; set; }

        /// <summary>
        /// The title shown above the form field
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// The description shown below the field for text based input,
        /// and next to the field for checkboxes and radio items
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// The value of the field that is prefilled.
        /// For checkboxes, radios and select items, this is the preselected item,
        /// and as such must have an appropriate key in <see cref="Options"/>.
        /// It is not possible to select multiple checkbox items as of now
        /// </summary>
        /// <remarks>Checkboxes cannot be preselected</remarks>
        public string? DefaultValue { get; set; }

        /// <summary>
        /// Whether the field must be filled or not.
        /// For checkboxes, this means at least one has to be checked
        /// </summary>
        public bool Required { get; set; }

        /// <summary>
        /// Extra options for the given field.
        /// <br /><br />
        /// <see cref="FieldType.Checkbox"/>,
        /// <see cref="FieldType.Radio"/>,
        /// <see cref="FieldType.Select"/>:
        /// Available values for selection.
        /// The key is the value for submission and the value for displaying to the user.
        /// Clients are required to support this.
        /// <br /><br />
        /// <see cref="FieldType.TextSingle" />:
        /// Suggestions for values (see "datalist" in HTML).
        /// Optional to support in client.
        /// <br /><br />
        /// <see cref="FieldType.File"/>:
        /// Suggested file extensions and groups. Example: key="audio files", value="wav,mp3,ogg".
        /// Extensions may be provided with or without leading dot.
        /// Optional to support in client.
        /// <br /><br />
        /// Other types: Ignored
        /// </summary>
        /// <remarks>
        /// If this is left empty, it results in unusable checkboxes, radio and select elements
        /// and therefore will fail validation for these field types.
        /// </remarks>
        public Dictionary<string, string> Options { get; } = [];

        /// <summary>
        /// The field type
        /// </summary>
        public FieldType FieldType { get; set; } = FieldType.TextSingle;

        /// <summary>
        /// Validates this instance
        /// </summary>
        [MemberNotNull(nameof(Name))]
        public void Validate()
        {
            if (string.IsNullOrEmpty(Name))
            {
                throw new Exception("Field name has not been set");
            }
            if (!Enum.IsDefined(FieldType))
            {
                throw new Exception("Unknown field type");
            }
            if (requiredOptions.Contains(FieldType))
            {
                if (Options.Count == 0)
                {
                    throw new Exception($"Field type {FieldType} requires at least one option");
                }
                if (DefaultValue != null && !Options.TryGetValue(DefaultValue, out _))
                {
                    throw new Exception($"Default value of field {Name} is not part of the permitted options");
                }
            }
        }

        /// <summary>
        /// Serializes this instance into a form field text.
        /// This automatically calls <see cref="Validate"/>
        /// </summary>
        /// <returns>Serialized form</returns>
        public string Serialize()
        {
            Validate();
            var sb = new StringBuilder();
            sb.AppendLine($"[{Uri.EscapeDataString(Name)}]");
            sb.AppendLine($"type={FieldType}");
            if (!string.IsNullOrEmpty(Title))
            {
                sb.AppendLine("title=" + Uri.EscapeDataString(Title));
            }
            if (!string.IsNullOrEmpty(Description))
            {
                sb.AppendLine("desc=" + Uri.EscapeDataString(Description));
            }
            if (!string.IsNullOrEmpty(DefaultValue))
            {
                sb.AppendLine("default=" + Uri.EscapeDataString(DefaultValue));
            }
            if (Required)
            {
                sb.AppendLine("required=y");
            }
            foreach (var opt in Options)
            {
                sb.AppendLine(string.Format("opt:{0}={1}", Uri.EscapeDataString(opt.Key), Uri.EscapeDataString(opt.Value)));
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Form field type
    /// </summary>
    public enum FieldType
    {
        /// <summary>
        /// Text field, like HTML type="text"
        /// </summary>
        TextSingle = 1,
        /// <summary>
        /// Text field like HTML textarea element
        /// </summary>
        TextMulti = 2,
        /// <summary>
        /// Password field like HTML type="password"
        /// </summary>
        Password = 3,
        /// <summary>
        /// Checkbox field like HTML type="checkbox"
        /// </summary>
        Checkbox = 4,
        /// <summary>
        /// Radio field like HTML type="radio"
        /// </summary>
        Radio = 5,
        /// <summary>
        /// Select field like HTML select element
        /// </summary>
        Select = 6,
        /// <summary>
        /// File field like HTML type="file"
        /// </summary>
        File = 7
    }
}
