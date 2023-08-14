# Gemini+ Multi Forms

One feature of gemini+ is the support for queries with multiple values.
this emulates HTML forms, but to keep it simple, does not permit styling, or ordering of the elements.
Many HTML form attributes are not available, and some form element types are not available either.

## Sending a Form Request

A server can send a form request to a gemini client as a standard response to any made client request.

The mime type is `text/gemini+form`, and the format is INI (see `SERVER.md` for details on that format).

## Form INI Format

The form ini consists of:

- An optional preamble
- Optional global values
- One or more form elements

## Preamble

The preamble is used to inform users of non-conforming clients,
that the server likes to use a feature they don't support.
In general, this can be any text that is not a valid section or setting.

Example preamble: 

`# This is a gemini+ form. If you see this text it means you don't have a gemini+ compatible client.`

## Global Values

A form may contain an empty section `[]` where global values are declared.

The settings currently available are:

- **maxsize**: Limits the total size of the form to the given number of bytes. This value is compared against the serialized form data, which includes the keys and the `&` delimiters. Negative values or zero indicate no size restriction. If this field is absent, there is no size restriction
- **target**: The url where the form is to be sent to. If absent, the form is sent back to the current url. The url must be a gemini or gemini+ url. If a gemini url, the client should silently change it to gemini+

## Form Fields

Each field begins with a section that represents the name of the field.
The section is then followed by one or more keys:

- **type**: Type of form element (see below)
- **title**: Title to be shown next to or above the element
- **desc**: More detailed description to be show below the form element
- **default**: Default prefilled/preselected value
- **required**: Whether the field must be filled/checked/selected by the user
- **opt:...**: Additional field specific options

Options are custom `name=value` fields that are prefixed with `opt:`

Title and description are optional. If not given, or empty,
the client should not render placeholders for them.

### Type: TextSimple

A simple text box. This type has no options

### Type: TextMulti

A textbox that supports multiple lines. This type has no options

### Type: Password

Like TextSimple, but the input is masked

### Type: Checkbox

A checkbox that the user can check. This type has options.

Each option is a checkbox with the key being the value that is submit,
and the value the text that's displayed to the user.
Said text is ideally clickable to check/uncheck the checkbox.
A checkbox always has at least one option.
The "default" setting can be used to preselect the appropriate checkbox.
Only one can be checked this way.

If the "required" option is set, the box must be checked for form submission

### Type: Radio

Behaves like the checkbox type, but only one option can be selected at a time.

### Type: Select

Behaves like the radio type, but is shown as a drop down instead of individual radio boxes

### Type: File

Ability to upload a file to the server. Arguably the most complex field. This type has options.

The options provide file type suggestions to the client.
A client MAY chose to ignore these, or combine them into one list.
The key is the value displayed to the user, and the value is a comma separated list of file extensions,
with or without the leading dot.

Example as found in an INI line: `opt:Documents=txt,doc,rtf,docx`

### Submit button

The form definition lacks a submit button.

A client MUST add this button manually,
or provide other means to submit the form (for example via ENTER key)

## Form Encoding

The form data from the client is encoded using standard url query format.
The client has no obligation to retain the field order when crafting the query string.

### Identical Names

Fields with identical names will appear multiple times in the encoded query.
If two checkboxes with the name "box" exist, and both are checked,
then the query string will be `?box=value1&box=value2`

### Files

Files are encoded across 3 query fields. A file field named "test" will be encoded as follows:

`?test=testfile.txt&test.size=1234&test.index=1`

- **test**: Client supplied file name
- **test.index**: File index in the body data
- **test.size**: File size in bytes

Although multiple file fields can share the same name,
it's not recommended because clients are not enforced to obey the ordering of the file fields.
Clients MUST always put "index" and "size" subfields DIRECTLY after the main field.
Clients MAY swap the ordering of "index" and "size".
Servers SHOULD reject requests that are inconsistant,
such as multiple size fields for the same file, files where size and/or index is missing.

The index begins at 1, and increases by 1 for every extra file,
which means the largest index matches the number of files sent to the server.

#### File Data

File data is encoded into the request body "as-is",
that is as raw octets. All files are concatenated without delimiter.
To read the files, the server MUST order the fields by index,
then read "size" bytes of each file to obtain them.

### Query in Body

For forms where the data may exceed the request line length,
the client may chose to put it into the body, provided the server signaled support for this.

Body form data is achieved by encoding a fake file element with index 0 into the query string of the url
as the only field. The file name and field names are not evaluated.
The field name SHOULD be just a single character, and the file name SHOULD be empty.
Other form fields MUST NOT be present in the url when using this method of form submission.

Example: `?_=&_.size=123&_.index=0`

The size in this case is the size in octets of the UTF-8 encoded form data.
This includes the entire length of the serialized data, including the field names.

The actual form fields are stored in the request body before any user submitted file is written to the request body.
The form is still to be properly url query encoded, but the leading `?` MUST be dropped.

### Reading Request Body

To read the request body, the server MUST first decode the URL form fields.
If the server detects that the query is stored in the request body (see previous chapter),
it MUST read that portion of the body and then decode the parameters again.

To read files, the server MUST decode all file fields,
and order them by the index field in ascending order.
The server MUST then perform a sanity check to ensure that the indexes start at 1,
and increment by one for each file.

When the server ensured, that the file form fields are valid,
it MAY then either read or discard files from the body data in the index order.

### Early Request Termination

The server MAY terminate the request early if it detects that the size of the form was exceeded,
or if a client is attempting to submit files when none were expected.

Whether to just terminate the connection or send a gemini error response first is at the servers discretion.
