# Gemini Protocol

This is a simplified Gemini protocol description with additional information for implementations.
It's recommended for people that have general knowledge of network protocols.

## Terminology Used in this Document

- Client: Application that consumes Gemini services
- Server: Application that provides Gemini services
- URI: Uniform Resource Locator
- CRLF: ASCII characters "Carriage Return" followed by "Line Feed". Hexadecimal: `0D0A`
- Space: ASCII space character. Hexadecimal: `20`

## URI

The Gemini protocol defines its own URI scheme. The scheme is identical to that of HTTP and HTTPS,
with the following exceptions:

- The scheme is "gemini"
- The default port is 1965
- The fragment has no meaning

### Examples

- Example complex HTTP URI: http://example.com/path?query#fragment
- Example complex Gemini URI: gemini://example.com/path?query

## Protocol Basics

The protocol stack as far as Gemini is concerned: Gemini on top of TLS on top of TCP.

### TCP

The default TCP port is 1965, but a Gemini server MAY run on any permitted TCP port.

### TLS

Gemini is built with recent developments of the TLS protocol in mind.
As such, servers SHOULD accept **TLS 1.3** or any later version.
They MAY accept **TLS 1.2**. They SHOULD NOT accept older protocol versions.

Gemini is built with TLS client authentication in mind.
Clients SHOULD support TLS client authentication using certificates.
Servers MAY request a certificate from the client.
Servers MAY terminate the connection if client authentication is requested,
but no certificate was supplied by the client.

### Gemini

- Default encoding for first request and response line: UTF-8
- UTF-8 byte order mark: No, never
- Default content type: text/gemini
- Default line ending: CRLF

Clients MAY support additional encodings,
and servers MAY present data in different encodings,
except for the text/gemini type, which MUST always use UTF-8.

Gemini session sequence overview:

1. Client connects to server using TCP
2. Client and server authenticate using TLS
3. Client sends a single line consisting of the full URI, terminated by CRLF
4. Server sends a single line consisting of the status code and either a mime content type identifier, or a freely chosen line of text, terminated by CRLF
5. For successful responses, the server MAY send a response body after the first line
6. The server terminates the connection

## Format of a Request Line

```
    <URI><CRLF>
```

The request line sent from a client to the server consists solely of the full Gemini URI,
including host name and scheme, terminated by a CRLF.

### Security Considerations

Servers MUST abort the connection if the client attempts to send a line that exceeds reasonable limits.
Servers SHOULD terminate the connection if the client takes an unreasonable amount of time to send the request line.

Reasonable defaults MAY vary between implementations.
This document suggests not accepting request lines that exceed approximately 4000 bytes,
and terminate connections that send less than 1000 bytes per second.

## Format of the Response Line

One of:

```
	<StatusCode><CRLF>
	<StatusCode><Space><CRLF>
	<StatusCode><Space><Extra Info><CRLF>
```

The response line sent by the server MUST begin with a status code that consists of a two digit decimal number
encoded using the appropriate ASCII characters 0-9 (Hexadecimal range: `30 - 39`).
To avoid problems with codes that contain leading zeroes,
codes `00` up to and including `09` are not allocated.

*The permitted status codes are explained further below.*

All status codes MAY be followed by a space, followed by zero or more characters, except for CRLF,
which exclusively serves as the terminator of the response line.

The extra information has a different meaning depending on the status code.
For a "success" status code, the extra info is mime type information.
For all other codes, the extra information is a line of text that the client SHOULD show to the user.
The mime information SHOULD always be provided on "success" status codes.

If the mime information is absent in "success" status codes,
the client SHOULD assume that the information is `text/gemini; charset=utf-8`

### Security Considerations

Clients SHOULD abort processing if the status line exceeds a reasonable length.

The Gemini protocol DOES NOT permit unallocated status codes to be used.
Status codes are grouped by the first digit,
and Gemini clients SHOULD treat unknown codes as the primary code of the group.
The primary code is the code with zero as the second digit. Examples: 10, 20, 30, ...

Clients MAY refuse to process the response at all if an unknown code is encountered,
and may simply return an error to the user that informs them of the protocol violation.

## Format of the Response Body

On successful response codes, the server MAY send additional data in form of a response body.
The response body has no special requirements for encoding the content.
It is the raw representation of the data that the server wants to send to the client.
The response body ends when the server terminates the connection.

### Security Considerations

Clients SHOULD read response bodies in a binary safe manner.

The response body has no limit in size.
A client SHOULD impose reasonable limits to the parsing of the body data
to ensure the client stays responsive to user input, and to not exhaust local resources.

The client SHOULD terminate the connection if the process is overwhelmed by the data and about to run out of memory.

## On Complete Response

The server MUST close the connection after a client request has been answered.
There is no mechanism to keep connections alive.
Clients MAY permit user interaction with content while said content is still being transmitted.

### Security Considerations

Clients SHOULD close the connection after an appropriate time if no data flows.

## Status Codes

The chapters below explain all known status codes.
unless otherwise specified, the codes MUST NOT contain a response body.

### 10 Input

The server requires input. The status line contains user instructions.
The client should prompt the user for a single line of input,
then repeat the request with the input in the URI query string.
If a query string is already contained in the current URI,
the new input replaces the old query string

Example how the URI changes with the input `1+2=3`:

- No existing query: `gemini://example.com/path` --> `gemini://example.com/path?1%2B2%3D3`
- Existing query: `gemini://example.com/path?test` --> `gemini://example.com/path?1%2B2%3D3`

### 11 Sensitive Input

This code behaves identically to code `10`,
except the input field should behave like a password field and mask the user input

### 20 Success

The server successfully processed the request.
The status line contains the mime type.
The body contains the response data.

### 30 Temporary Redirect

The status line contains a new URI.
The URI MAY be relative to the current URI.
The client SHOULD repeat the request with the new URI.

#### Security Considerations

Clients MUST contain protections against too many redirects.
The suggested redirect limit is 5, but clients MAY support more.

### 31 Permanent Redirect

This code behaves identically to code 30.
The client SHOULD cache the redirected URI indefinitely,
and automatically request the new target URI when a request to the old URI is made.

### 40 Temporary Failure

The request could not be processed by the server,
and the issue may resolve itself in the future.
A client MAY repeat the request again on user request.

The status line MAY contain more detailed error information.

### 41 Server Unavailable

The server is currently unable to handle the user request.
Behaves like code 40.
This code is appropriately to be used when a server backend is down,
or undergoing maintenance.

### 42 CGI Error

The server acting as a reverse proxy on behalf of the client
was unable to obtain a valid response from the backend.
Behaves like code 40.
This code is appropriate for when the backend is responding to requests but sends invalid response data
that violates gemini protocol.

### 43 Proxy Error

The server acting as a forward proxy on behalf of the client
was unable to obtain a valid response from the supplied URI.
Behaves like code 40.

### 44 Slow Down

The server demands, that the client to temporarily stop making requests.
The status contains a sole integer that contains the number of seconds
during which the client MUST not make further requests.

### 50 Permanent Failure

Permanent version of code 40.
Repeating the request now or in the near future is likely to fail again.
The status line MAY contain more specific error information.
Clients SHOULD NOT repeat the request.

### 51 Not Found

The requested resource was not found.

**This code is misplaced in the 5x group, and is not a permanent error**

Servers should use code 40 instead to avoid confusion.

### 52 Gone

The requested resource is not found, but existed in the past,
and the server believes that this condition is likely to be permanent.
Behaves like code 50.

### 53 Proxy Request Refused

The server refuses to process the request because it doesn't serves
content for the host specified by the client in the URI.
Behaves like code 50.

### 59 Bad Request

The server considers the client request invalid
Behaves like code 50.

Reasons for this code vary, and may be due to causes including but not limited to:

- Leading or trailing whitespace in the URI
- Request line doesn't ends in CRLF but only CR or only LF
- The request line was empty
- The URI is malformed
- The URI is for a different protocol

### 60 Client Certificate Required

The client has made a request that the server refuses to answer,
because the client didn't supply a certificate in the TLS handshake.

This status code is considered permanent,
and client SHOULD NOT repeat the request without supplying a certificate.

The status line may contain more information for the user.

### 61 Certificate Not Authorized

The server doesn't accepts the client certificate.
The certificate is formally valid, but the server is refusing to trust the certificate.

If the certificate is formally invalid, code 62 should be used instead.

Reasons for this code vary, but the most likely reason is
that the server has a list of trusted certificates,
and the supplied certificate is not on that list.

The status line may contain the exact rejection reason.

### 62 Certificate Not Valid

The certificate is not valid.
This code indicates that there are problems with the certificate itself regardless of URI.
This status code is considered permanent,
and the client SHOULD NOT use this certificate again.

Reasons for rejection are subject to the servers discretion, but may include:

- Certificate not yet valid
- Certificate expired
- Unsafe signature algorithm parameters (i.e. key size too small)

The status line may contain the exact rejection reason.

## Certificates

The standard doesn't endorses trust in any well known certificate infrastructure.
Because of this, it's at the sole discretion of the client whether a certificate is to be trusted or not.

The suggested method is known as TOFU (Trust On First Use),
which means the first time a server is visited, the client should blindly trust the received certificate.

This is possibly a bad idea because it gives state actors and ISPs an easy way to undermine encryption.
Because of this, this document suggests that servers should use a well-known certificate.
Free certificates can be obtained automatically if one has access to an HTTP server and a domain.

For self signed certificates, the user should be prompted for trust.
