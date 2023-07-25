# Gemini

This project is a full Gemini client and server implementation.

## Client

### Features

*Features marked with an asterisk are either planned or under construction*

- Full implementation of the protocol with redirect loop protection
- Support for query prompts and password request when required by the remote
- Fairly robust against servers not fully protocol compliant
- Rendering of gemini files
- Uses your existing web browser
- Displays images, audio, and video content right in your browser
- HTTP(S) links open in new tabs
- Backward and forward navigation using your browsers history function
- Supports linking to other protocols (provided you have handlers for them)
- Whitelisting of custom server certificates
- Client certificate authentication

### How to use

There are currently no pre-built binaries yet.
You can manually run it without a code editor:

1. [Install the .NET 6](https://dotnet.microsoft.com/en-us/download) if you don't have it yet (use the "SDK" version)
2. Clone this repository or download it as zip
3. Launch the `run.bat` file in the main directory

This will build the application and run it on a random local port,
then it opens your default browser

### Query

The server may ask you for a query or a secret using a single line of descriptive text.
In those cases you will be shown an input field.
Simply input the requested value and press `ENTER` to submit the value.

### Server certificates

Most gemini servers run on self signed certificates.
When you visit such a service, you will be asked to trust the certificate.
It's up to you, whether you want to accept or reject it.
Trusting the certificate will only apply to this gemini browser and not other tools.

If the certificate is expired,
you can trust it but the trust is lost once you exit the application.

You're only asked to trust a certificate
if your operating system won't already trust it.

### Client certificates

*This is not yet implemented but documents how it will be*

Client certificates are how you authenticate yourself (login) to a gemini server.
Most servers don't require this.
If a server requires this, you will be prompted to select one of your identities.
If you don't yet have one, an error message will be shown.

The application will always make anonymous attempts first.

### Planned Features

- Bookmarks
- Start page configuration

## Server

The server currently only serves static files but is otherwise fully protocol compliant,
including requesting client certificates.

The server is easily extendable by implementing `Gemini.Lib.GeminiHost` and registering it in the server.
In the future the server will automatically search for host implementations and load them.
