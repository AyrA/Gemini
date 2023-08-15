# GEMINI+

Gemini+ is an extension to the gemini protocol.
It adds a few more advanced features.

## Gemini+ URL scheme

If a client is aware that a server supports Gemini+,
it can use `gemini+` as url scheme to communicate with the server if it wants to.
A client SHOULD NOT blindly use a gemini+ url supplied by the user,
but instead SHOULD always query the server for gemini+ support first.

A client MAY still use `gemini` scheme if it does not plans on using any of the features of `gemini+`

## Detecting Gemini+

Gemini+ is detected by sending an empty request,
which consists of a sole CRLF.

A server that supports gemini+ will send a success response.
A server that doesn't supports this, should return an error,
since the line does not contain a valid gemini url.
How client side Gemini+ features are communicated to the server is specified further below.

A client SHOULD cache knowledge about gemini+ support for an appropriate amount of time.

## Gemini+ Detection Response

The response to a gemini+ detection request is made using the mime type `text/gemini+info`

The response body consists of an INI style document.

## The INI File Format

The INI file format is a simple, line based text file.
A line is either a setting or a heading.
A heading is indicated by the line starting with `[` and ending with `]`.
The value between the brackets is the header name.
After a header, zero or more setting lines in the format `key=value` follow that configure the header.
Unless specified, the order of the headings and their settings is irrelevant.

Lines purely consisting of whitespace are ignored.

### Gemini+ Specific INI

Gemini uses a reduced set of INI. A few things are not allowed,
and a few things may differ from other INI systems:

1. There is no support for comments
2. Settings are not allowed before the first header
3. Settings that use boolean values use `y` and `n` as value
4. Headers and setting keys are case insensitive
5. Header names, keys, and values are url encoded
6. File encoding is always UTF-8
7. Syntactically invalid INI data should cause all INI data to be discarded

*Note: Strictly speaking, url encoding is only necessary if it would create ambiguity.*
*This includes, but is not limited to control characters, or keys with '=' in them.*
*Keys and values with leading and/or trailing whitespace should have at least the whitespace encoded.*

- Regex to validate and parse a section: `^\[([^\r\n]*)\]$`
- Regex to validate and parse a setting: `^([^\r\n=]*)=([^\r\n]*)$`

Spaces are to be encoded as `%20`, and not `+`

## Gemini+ Detection INI

The response to a gemini detection request consists of an INI body with the following content.

*An absent header or setting is to be interpreted as a not supported feature.*

### Section: FORM

This section describes features in regards to form submission.

#### Value: Multi

Boolean; If enabled means the server supports queries with multiple values,
similar (but more limited) to forms in HTML.

#### Value: Files

Boolean; The server supports file uploading

This has only an effect if `Multi=y` and `Stream=y`

#### Value: Stream

Boolean; The server supports content in the body of the client request.

This has only an effect if `Multi=y`, and is required for `Files=y`

### Section: META

This defines extra features in the meta line (the remaining part after the status code in a response)

#### Value: Extended

Boolean; If set, the server supports extended meta line.

An extended meta line contains additional data after the mime type.
In general, it's assumed that all servers support this,
because they often send `charset=utf-8` as additional value.

Extended meta data is additonal `key=value` pairs, delimited by semicolons that are added to the meta line.
The keys are case insensitive.

Example: `text/gemini; Charset=utf-8; Size=1234; LastModified=2023-01-01T00:00:00Z; Filename="Test File.gmi"`

The line indicates that the server is sending a gemini document with UTF-8 encoding.
The size is 1234 bytes, and the document was last modified on midnight on new years eve 2023 (UTC time zone).
If the file is subject to a download to disk, the suggested file name is "Test File.gmi".

Known values:

- **Charset**: Encoding of the content. Only meaningful for text content, and then usually UTF-8
- **Size**: Size of the body data in bytes (see BODY section for details). Useful for progress indicators or file transfer verification
- **LastModified**: ISO 8601 timestamp of when the resource was last modified. If the file is downloaded to disk, this value should be set on the file, provided it's within the range supported by the file system, and not in the future.
- **Filename**: Suggested file name for file downloads. Clients should remove path information from this, and sanitize the file name to make it valid. Suggestion is to replace invalid characters with `_`
- **Range**: For range requests (see BODY section for details) the byte range of the content that's sent to the client

Values containing whitespace should be enclosed in double quotes.

*Note: The meta line is textual status information on any other response than "success".*
*Because of this, extended meta attributes are only possible on successfull responses.*

### Section: BODY

Features considering the format and capabilities of the response body

#### Value: Compress

Boolean, String; This can be "n" to disable,
or a comma separated list of compression algorithms supported by the server.
Has no effect if extended meta is not enabled.

Defined at the moment are:

- **gz**: GZip compression
- **br**: Brotli compression
- **xz**: LZMA compression

If no value is reported by the client, or the requested value is not supported,
the server MUST NOT use compression.
The server MAY deny the request if the resource only exists in compressed format.
The client selected algorithm is communicated back via the `body.compress` url fragment.
The client may inidicate support for multiple values by supplying them as comma separated list of values.

#### Value: Range

Boolean; Indicates that the server supports range requests.
This has no effect if the extended meta setting is not enabled.

Support for range requests for a resource can be indicated by the "Size" extended meta attribute.
However, servers are not required to actually offer range requests when the size attribute is present.

A client can request a byte range from a resource by using the fragment `body.range`
with any number of comma separated ranges as value.

A range is formatted as `offset:count` where offset and count are integers.
Offset is the zero based byte offset from the start of the content,
count is the number of bytes requested from offset.

Offset and count may be prefixed with `-` to make them negative.
A negative offset indicates that this offset is from the end of the content,
rather than the start.
A negative count indicates how many bytes before the end to stop.
`-0` is an acceptable count value that indicates to read all remaining bytes up to the end.

All values are applied to the raw size of the resource,
before any compression is applied.

**Examples with a 100 byte sized file:**

- `10:20`: Skip 10 bytes, then read 20 bytes
- `10:-20`: Skip 10 bytes, then read 70 bytes
- `10:-0`: Skip 10 bytes, then read 90 bytes
- `-10:3`: Skip 90 bytes, then read 3 bytes
- `-10:-3`: Skip 90 bytes, then read 7 bytes

**Examples of invalid ranges with a 100 byte sized file:**

- `-10:-20`: Discarded. End of the range would be before the start
- `90:-20`: Discarded. End of the range would be before the start
- `120:10`: Discarded. Offset is outside of bounds
- `20:100`: Discarded. End is outside of bounds

The server MUST discard invalid ranges that land outside of the boundaries
or are nonsensical. If not a single useful range remains,
the full content SHOULD be sent to the client.

To communicate to the client, which ranges where honored,
the "Range" extended META attribute can be used.
The syntax for the value is identical to the syntax in the client range request.
The order of the ranges must match the order in which they're sent to the client.

An absent "Range" attribute indicates to the client that no ranges where honored,
and the body contains the full content.

The server MAY limit the number of ranges per request.
The server MAY reorder the ranges.
The server MUST either match the requested range exactly or reject it entirely.

A range with a computed length of zero causes no data to be sent.
This effectively turns a range request of `0:0` into a request for the META line
without any content. This is comparable to an HTTP "HEAD" request.

### Section: TCP

This section contains connection specific values

#### Value: Keepalive

Boolean; Indicates whether keepalive is supported by the server or not.

If supported by the server, clients can indicate via fragment `tcp.keepalive` (no value)
that they're interested in using this feature.
After a server response, the connection will stay open,
and the client can send additional requests.
How long an idle connection is left open is at the servers sole discretion.

Clients that utilize keepalive MUST implement error recovery
for when a connection was just closed as they were sending the request.
In those scenarios, the error SHOULD NOT be reported to the user immediately,
but a new connection attempt is to be made first.

Note: This only works reliably if the server sends the size value back to the client,
otherwise it's not possible to determine the end of the body data for the client.
If the server cannot reliably set the size value, it should close the connection after the response,
regardless of the client preferences.

#### Value: Raw

Boolean, Integer; Indicates whether raw (unencrypted) connections are supported.
This setting is either "n" to indicate no support,
otherwise it's a port number where unencrypted connections can be made.

Clients SHOULD ignore this value and always use TLS unless a user specifically requests otherwise.

The use of TLS can be an unnecessary overhead,
especially if the server runs behind a reverse proxy,
in which case an unencrypted variant is to be preferred,
especially if the machines are connected via a virtual cable.

## Fragment Based Client Feature Request

Because there are no request headers, a client can indicate a desired feature by using the url only.
The fragment is an optional part of an url and always comes last.
It begins with `#` and is followed by freeform text.
Under normal circumstances, a client SHOULD NOT transmit this value to the server;
It is only supported on `gemini+` urls, not `gemini` urls.

For feature declaration, the query style syntax is used in the fragment.

Example: `gemini+://example.com/SomeUrl?a=1#tcp.keepalive&body.compress=br`

- The client is using `gemini+` for this request
- The client requests `example.com/SomeUrl` from a gemini server
- The client sends query value `a=1` to the server
- The client wants the server to compress the body
- The client wants the server to keep the connection open

### Security consideration

If the client is presented with a gemini+ url that contains fragment data,
it MUST drop unknown and/or unsupported keys from the url.

Being a gemini+ feature only, clients MUST remove the fragment from regular gemini urls.

## Multi Forms

*See FORMS.md for details*

## Implementation considerations

Gemini+ is on purpose kept as compatible as possible to plain gemini.
Because of this, no real header system like that of HTTP has been included.
This also solves the problem of HTTP headers having grown out of proportion,
especially the User-Agent and Cookie header.
Using the URL limits headers to how long of an url the server is willing to accept.