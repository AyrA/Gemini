namespace Gemini.Lib
{
    public enum StatusCode
    {
        /// <summary>
        /// The requested resource accepts a line of textual user input.
        /// The &lt;META&gt; line is a prompt which should be displayed to the user.
        /// The same resource should then be requested again with the user's input included as a query component.
        /// Queries are included in requests as per the usual generic URL definition in RFC3986,
        /// i.e. separated from the path by a ?.
        /// Reserved characters used in the user's input must be "percent-encoded" as per RFC3986,
        /// and space characters should also be percent-encoded.
        /// </summary>
        Input = 10,
        /// <summary>
        /// As per status code 10, but for use with sensitive input such as passwords.
        /// Clients should present the prompt as per status code 10,
        /// but the user's input should not be echoed to the screen
        /// to prevent it being read by "shoulder surfers".
        /// </summary>
        SensitiveInput = 11,
        /// <summary>
        /// The request was handled successfully and a response body will follow the response header.
        /// The &lt;META&gt; line is a MIME media type which applies to the response body.
        /// </summary>
        Success = 20,
        /// <summary>
        /// The server is redirecting the client to a new location for the requested resource.
        /// &lt;META&gt; is a new URL for the requested resource.
        /// The URL may be absolute or relative.
        /// If relative, it should be resolved against the URL used in the original request.
        /// If the URL used in the original request contained a query string,
        /// the client MUST NOT apply this string to the redirect URL, instead using the redirect URL "as is".
        /// The redirect should be considered temporary,
        /// i.e. clients should continue to request the resource at the original address
        /// and should not perform convenience actions like automatically updating bookmarks.
        /// There is no response body.
        /// </summary>
        TemporaryRedirect = 30,
        /// <summary>
        /// The requested resource should be consistently requested from the new URL provided in future.
        /// Tools like search engine indexers or content aggregators should update their configurations
        /// to avoid requesting the old URL, and end-user clients may automatically update bookmarks, etc.
        /// Note that clients which only pay attention to the initial digit of status codes
        /// will treat this as a temporary redirect. They will still end up at the right place,
        /// they just won't be able to make use of the knowledge that this redirect is permanent,
        /// so they'll pay a small performance penalty by having to follow the redirect each time.
        /// </summary>
        PermanentRedirect = 31,
        /// <summary>
        /// The request has failed. There is no response body.
        /// The nature of the failure is temporary, i.e. an identical request MAY succeed in the future.
        /// The contents of &lt;META&gt; may provide additional information on the failure,
        /// and should be displayed to human users.
        /// </summary>
        TemporaryFailure = 40,
        /// <summary>
        /// The server is unavailable due to overload or maintenance. (cf HTTP 503)
        /// </summary>
        ServerUnavailable = 41,
        /// <summary>
        /// A CGI process, or similar system for generating dynamic content, died unexpectedly or timed out.
        /// </summary>
        CgiError = 42,
        /// <summary>
        /// A proxy request failed because the server was unable to successfully complete a transaction
        /// with the remote host. (cf HTTP 502, 504)
        /// </summary>
        ProxyError = 43,
        /// <summary>
        /// Rate limiting is in effect.
        /// &lt;META&gt; is an integer number of seconds which the client must wait
        /// before another request is made to this server. (cf HTTP 429)
        /// </summary>
        SlowDown = 44,
        /// <summary>
        /// The request has failed. There is no response body.
        /// The nature of the failure is permanent,
        /// i.e. identical future requests will reliably fail for the same reason.
        /// The contents of &lt;META&gt; may provide additional information on the failure,
        /// and should be displayed to human users.
        /// Automatic clients such as aggregators or indexing crawlers should not repeat this request.
        /// </summary>
        PermanentFailure = 50,
        /// <summary>
        /// The requested resource could not be found but may be available in the future.
        /// (cf HTTP 404)
        /// </summary>
        NotFound = 51,
        /// <summary>
        /// The resource requested is no longer available and will not be available again.
        /// Search engines and similar tools should remove this resource from their indices.
        /// Content aggregators should stop requesting the resource
        /// and convey to their human users that the subscribed resource is gone. (cf HTTP 410)
        /// </summary>
        Gone = 52,
        /// <summary>
        /// The request was for a resource at a domain not served by the server
        /// and the server does not accept proxy requests.
        /// </summary>
        ProxyRequestRefused = 53,
        /// <summary>
        /// The server was unable to parse the client's request,
        /// presumably due to a malformed request. (cf HTTP 400)
        /// </summary>
        BadRequest = 59,
        /// <summary>
        /// The requested resource requires a client certificate to access.
        /// If the request was made without a certificate, it should be repeated with one.
        /// If the request was made with a certificate,
        /// the server did not accept it and the request should be repeated with a different certificate.
        /// The contents of &lt;META&gt; (and/or the specific 6x code)
        /// may provide additional information on certificate requirements or the reason a certificate was rejected.
        /// </summary>
        ClientCertificateRequired = 60,
        /// <summary>
        /// The supplied client certificate is not authorised for accessing the particular requested resource.
        /// The problem is not with the certificate itself, which may be authorised for other resources.
        /// </summary>
        CertificateNotAuthorized = 61,
        /// <summary>
        /// The supplied client certificate was not accepted because it is not valid.
        /// This indicates a problem with the certificate in and of itself,
        /// with no consideration of the particular requested resource.
        /// The most likely cause is that the certificate's validity start date is in the future
        /// or its expiry date has passed, but this code may also indicate an invalid signature,
        /// or a violation of X509 standard requirements.
        /// The &lt;META&gt; should provide more information about the exact error.
        /// </summary>
        CertificateNotValid = 62
    }
}
