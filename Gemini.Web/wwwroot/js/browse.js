"use strict";
(async function (q, qa, ce) {
    //Last used client cert id per host
    const lastId = {};
    const mimeTypes = [];
    const contentType = {
        unknown: 0,
        string: 1,
        bytes: 2
    };

    async function loadMime() {
        if (mimeTypes.length === 0) {
            console.debug("Loading mime types...");
            const lines = (await (await fetch("/mime.txt")).text()).trim().split('\n').map(v => v.trim()).filter(v => v.indexOf('#') < 0);
            const parsed = lines.map(v => v.match(/^(\S+)\s+(\S.+)$/)).filter(v => v && v.length).map(v => ({ mime: v[1], ext: v[2].split(/\s+/) }));
            mimeTypes.splice(0, mimeTypes.length, ...parsed);
            console.debug("Loaded", mimeTypes.length, "types");
        }
    }

    function setTitleFromUrl(url) {
        //JS url object is too stupid for gemini URLs even though they follow HTTP practices
        const matches = url.match(/^gemini:\/\/([^/]+)(.*)$/);
        if (matches) {
            document.title = matches[1] + " - " + matches[2];
        }
    }

    function fixUrl(url) {
        return getUrlParts(url).href;
    }

    function combineUrl(url, baseUrl) {
        const href = getUrlParts(baseUrl).href;
        return new URL(url, href).href;
    }

    function renderGemini(url, text) {
        const content = q("#gemini");
        const lines = text.trim().split(/\r?\n/);
        const elements = [];
        let preformat = false;
        url = fixUrl(url);
        lines.forEach(function (line) {
            if (preformat && !line.match(/^```/)) {
                console.debug("Adding", line, "to PRE");
                elements[elements.length - 1].insertAdjacentText("beforeend", line + "\r\n");
            }
            else if (line.match(/^###/)) {
                console.debug("H3");
                const e = ce("h3");
                e.textContent = line.substr(3);
                elements.push(e);
            }
            else if (line.match(/^##/)) {
                console.debug("H2");
                const e = ce("h2");
                e.textContent = line.substr(2);
                elements.push(e);
            }
            else if (line.match(/^#/)) {
                console.debug("H1");
                const e = ce("h1");
                e.textContent = line.substr(1);
                elements.push(e);
            }
            else if (line.match(/^=>/)) {
                console.debug("A");
                const match = line.match(/^=>\s*([\S]+)(?:\s+(.+))?$/);
                if (!match) {
                    console.warn("Invalid link line:", line);
                    return;
                }
                const e = ce("a");
                const linkUrl = combineUrl(match[1], url);
                if (linkUrl.match(/^gemini:/i)) {
                    e.dataset.url = linkUrl;
                    e.href = "/Gemini/Get?url=" + encodeURIComponent(linkUrl);
                }
                else {
                    e.href = linkUrl;
                }
                e.textContent = match[2] || match[1];
                e.setAttribute("target", "_blank");
                elements.push(e);
                elements.push(ce("br"));
            }
            else if (line.match(/^```/)) {
                preformat = !preformat;
                if (preformat) {
                    console.debug("PRE");
                    elements.push(ce("pre"));
                }
                else {
                    console.debug("PRE END");
                }
            }
            else if (line.match(/^>/)) {
                console.debug("BLOCKQUOTE");
                const e = elements[elements.length - 1]?.nodeName === "BLOCKQUOTE"
                    ? elements[elements.length - 1]
                    : (elements[elements.length] = ce("blockquote"));
                e.textContent += line.substr(1).trim() + " ";
            }
            else if (line.match(/^\*/)) {
                console.debug("LI");
                const e = elements[elements.length - 1]?.nodeName === "UL"
                    ? elements[elements.length - 1]
                    : (elements[elements.length] = ce("ul"));
                e.appendChild(ce("li")).textContent = line.substr(1).trim();
            }
            else {
                console.debug("Text line");
                elements.push(document.createTextNode(line));
                elements.push(ce("br"));
            }
        });
        content.innerHTML = "";
        elements.forEach(e => content.appendChild(e));
    }

    function getExt(mime) {
        const type = mimeTypes.find(m => m.mime === mime);
        return type?.ext?.[0] ?? "bin";
    }

    function getMime(ext) {
        const defaultType = "application/octet-stream";
        if (typeof (ext) !== typeof ("")) {
            console.warn("Non string argument passed to getMime:", ext);
            return defaultType;
        }
        ext = ext.toLowerCase();
        //Remove leading dot
        if (ext.indexOf('.') === 0) {
            ext = ext.substr(1);
        }
        return mimeTypes.find(m => m.ext.indexOf(ext) >= 0)?.mime ?? defaultType;
    }

    function addLink(data, type, mime, fileName) {
        if (type === contentType.unknown) {
            return null;
        }
        if (!fileName) {
            fileName = "data." + getExt(mime);
        }
        let url = "data:" + mime + ";base64,";
        if (type === contentType.bytes) {
            url += data;
        }
        else {
            url += btoa(data);
        }
        const p = q("#gemini").appendChild(ce("p"));
        const a = p.appendChild(ce("a"));
        a.href = url;
        a.textContent = "Download " + fileName + " (" + mime + ")";
        a.setAttribute("download", fileName);
        a.classList.add("btn", "btn-primary");
    }

    //This is a bit like the URL constructor, but actually working
    function getUrlParts() {
        const defaultPort = 1965;
        const raw = q("[type=url]").value;
        const m = raw.match(/^([^:]+):\/\/([^\/?#:]*)(?::(\d+))?(?:(\/[^?#:]*))?(?:\?([^#:]*))?(?:#(.*))?$/);
        const url = {
            scheme: m[1],
            host: m[2],
            port: Math.min(0xFFFF, Math.max(0, (m[3] | 0))),
            path: m[4] ? m[4] : "/",
            query: m[5],
            hash: m[6]
        };
        if (url.port === 0) {
            url.port = defaultPort;
        }
        url.isDefaultPort = url.port === defaultPort;
        url.origin = url.scheme + "://" + url.host + (url.isDefaultPort ? "" : ":" + url.port);
        url.href = url.origin + url.path;
        if (url.query) {
            url.href += "?" + url.query;
        }
        if (url.hash) {
            url.href += "#" + url.hash;
        }
        console.debug("Parsed", raw, "into", url);
        return url;
    }

    function getUrl() {
        return getUrlParts(q("[type=url]").value).href;
    }

    function setUrl(url) {
        return q("[type=url]").value = url.href || url;
    }

    function tryParse(text) {
        try {
            return JSON.parse(text);
        } catch (e) {
            //Return fake element with the text as content
            return {
                statusCode: -1,
                isSuccess: false,
                isError: true,
                isRedirect: false,
                content: text,
                meta: "Failed to decode JSON"
            }
        }
    }

    function getFileNameFromUrl(url) {
        const fileName = (url.split('#')[0].split('?')[0].match(/([^/]+)\/?$/) || [])[1];
        return fileName;
    }

    function saveHistory() {
        setTitleFromUrl(getUrl());
        history.pushState({}, "", location.pathname + "#" + getUrl());
    }

    async function render(redirectLimit) {
        const fd = new FormData(q("#form"));
        const currentCert = lastId[getUrlParts().origin];
        if (currentCert?.id) {
            fd.append("certificate", currentCert.id);
            if (currentCert.password) {
                fd.append("password", currentCert.password);
            }
        }
        renderGemini(getUrl(), "## Fetching " + getUrl() + "...");
        const response = await fetch(q("#form").action, {
            method: "POST",
            body: fd
        });
        const text = await response.text();
        const json = tryParse(text);
        console.debug("API result:", json);
        if (json.isSuccess) {
            saveHistory();
            const mime = json.mimeInformation?.mimeType ?? "application/octet-stream";
            const fileName = json.mimeInformation?.extraInfo?.filename ?? getFileNameFromUrl(getUrl());
            if (mime === "text/gemini") {
                renderGemini(getUrl(), json.contentType === contentType.bytes ? window.atob(json.content) : json.content);
            }
            else if (mime === "text/plain") {
                q("#gemini").innerHTML = "<pre></pre>";
                q("#gemini pre").textContent = json.contentType === contentType.bytes ? window.atob(json.content) : json.content;
                addLink(json.content, json.contentType, mime, fileName);
            }
            else if (mime === "text/html") {
                renderGemini(getUrl(), "The gemini server sent an HTML document");
                addLink(json.content, json.contentType, mime, fileName);
            }
            else if (mime.match(/^image\/.+/)) {
                q("#gemini").innerHTML = "<h1>The server sent an image</h1>";
                const img = document.createElement("img");
                img.src = "data:" + mime + ";base64," + json.content;
                img.style.maxWidth = "100%";
                q("#gemini").appendChild(img);
                addLink(json.content, json.contentType, mime, fileName);
            }
            else if (mime.match(/^audio\/.+/)) {
                q("#gemini").innerHTML = "<h1>The server sent audio data</h1>";
                const audio = document.createElement("audio");
                audio.controls = true;
                audio.src = "data:" + mime + ";base64," + json.content;
                q("#gemini").appendChild(audio);
                audio.play();
                addLink(json.content, json.contentType, mime, fileName);
            }
            else if (mime.match(/^video\/.+/)) {
                q("#gemini").innerHTML = "<h1>The server sent video data</h1>";
                const video = document.createElement("video");
                video.controls = true;
                video.src = "data:" + mime + ";base64," + json.content;
                q("#gemini").appendChild(video);
                video.play();
                addLink(json.content, json.contentType, mime, fileName);
            }
            else {
                renderGemini(getUrl(), "# ERROR\r\nNo renderer is defined for data of type " + mime +
                    "\r\nYou can download it and try to find a local application to open it");
                addLink(json.content, json.contentType, mime, fileName);
            }
        }
        else if (json.isInput) {
            saveHistory();
            q("#gemini").innerHTML = "<h1>Information required</h1>";
            const p = q("#gemini").appendChild(ce("p"));
            p.textContent = "The website requests information from you to process your request: " + json.meta;
            const input = q("#gemini").appendChild(ce("input"));
            if (json.statusCode === 11) {
                input.setAttribute("type", "password");
                input.setAttribute("autocomplete", "off");
            }
            input.addEventListener("keydown", function (e) {
                if (e.keyCode === 13) {
                    e.preventDefault();
                    setUrl(getUrl().split('?')[0] + "?" + encodeURIComponent(input.value));
                    q("#form").requestSubmit();
                }
            });
        }
        else if (json.isRedirect) {
            if (redirectLimit > 0) {
                const url = new URL(json.meta, getUrl());
                if (url.href === getUrl()) {
                    renderGemini(getUrl(), "# PROTOCOL VIOLATION\r\nThe page redirects onto itself");
                }
                else {
                    if (url.protocol !== "gemini:") {
                        renderGemini(getUrl(), [
                            "# Leaving gemini",
                            "The gemini server at " + getUrl() + " wants to redirect you to a non-gemini URL",
                            "To follow this redirect, click the link below",
                            "=> " + url.href
                        ].join("\r\n"));
                    }
                    else {
                        setUrl(url);
                        render(redirectLimit - 1);
                    }
                }
            }
            else {
                renderGemini(getUrl(), "# PROTOCOL VIOLATION\r\nThe page redirects too often");
            }
        }
        else if (json.isError) {
            saveHistory();
            renderGemini(getUrl(), "# " + json.statusCode + " SERVER ERROR\r\n```\r\n" + json.meta + "\r\n```");
        }
        else if (json.statusCode / 10 | 0 === 6) {
            const origin = getUrlParts().origin;
            renderGemini(getUrl(), "# " + json.statusCode + " CLIENT CERTIFICATE REQUIRED\r\n```\r\n" + json.meta + "\r\n```");
            const ident = await selectIdentity(lastId[origin]?.id, "The server at " + origin + " wants you to authenticate");
            if (ident?.id) {
                lastId[origin] = ident;
                render(redirectLimit);
            }
        }
        else if (json.statusCode === -2) {
            //Unknown certificate
            console.log(json.content);
            const dlg = new ezDlg(false);

            const p1 = ce("p");
            p1.textContent = "The server at " + json.content.host + " sent a certificate we cannot automatically trust. This usually means it's self signed or expired";
            p1.appendChild(ce("br"));
            p1.insertAdjacentText("beforeend", "Please review the values below and decide whether you want to trust it or not");

            const p2 = ce("p");
            p2.appendChild(ce("b")).textContent = "Issuer: ";
            p2.insertAdjacentText("beforeend", json.content.issuerName);

            p2.appendChild(ce("br"));
            p2.appendChild(ce("b")).textContent = "Name: ";
            p2.insertAdjacentText("beforeend", json.content.subjectName);

            p2.appendChild(ce("br"));
            p2.appendChild(ce("b")).textContent = "Id: ";
            p2.insertAdjacentText("beforeend", json.content.id);

            p2.appendChild(ce("br"));
            p2.appendChild(ce("b")).textContent = "Expires: ";
            p2.insertAdjacentText("beforeend", json.content.expires);

            const result = await dlg.confirm([p1, p2, "Add to trust list?"], "Unknown certificate", "Yes", "No");
            console.log(result);

            if (result) {
                const fd = new FormData();
                fd.append("base64Cert", json.content.certificate);
                const response = await fetch("/ServerTrust/Trust/" + encodeURIComponent(json.content.host), {
                    method: "PUT",
                    body: fd
                });
                if (response.status === 200) {
                    await dlg.alert("Certificate added to trust list", "Certificate trusted");
                    render(redirectLimit);
                }
                else {
                    await dlg.alert("Cannot add certificate. Server returned an error", "Server error");
                    q("#gemini").textContent = await response.text();
                }
            }
            else {
                renderGemini(getUrl(), "# CERTIFICATE ERROR\r\nRequest cancelled. If this was a mistake, redo the request.");
            }
        }
        else if (!json.isKnownCode) {
            saveHistory();
            //Negative numbers are locally generated
            if (json.statusCode < 0) {
                renderGemini(getUrl(), [
                    "# BACKEND ERROR",
                    "Your application ran into a problem when communicating with a gemini service.",
                    "Most likely causes for this are:",
                    "* Remote service is unavailable or overloaded",
                    "* A client certificate was requested by the remote server but none was provided by you",
                    "* Server is violating the protocol",
                    "## Details",
                    json.contentType === contentType.bytes ? window.atob(json.content) : json.content
                ].join("\r\n"));
            }
            else {
                renderGemini(getUrl(), "# PROTOCOL VIOLATION\r\nResponse code " + json.statusCode + " is not defined");
            }
        }
    }

    await loadMime();

    q("#form").addEventListener("submit", function (e) {
        e.preventDefault();
        render(5);
    });

    //Fix up URL when it's changed.
    //This turns human input into a fully qualified gemini URL
    q("#form [type=url]").addEventListener("change", function () {
        let v = this.value;
        if (!v.match(/^gemini:/)) {
            v = "gemini:" + v;
        }
        //Check if there's a trailing slash after the host name
        if (!v.match(/^gemini:\/\/[^\/]+\//)) {
            v += "/";
        }
        if (v != this.value) {
            this.value = v;
        }
    });

    //Watch over links from gemini pages and reroute the requests
    document.addEventListener("click", function (e) {
        if (e.target.nodeName === "A" && (e.target.dataset.url ?? "").match(/^gemini:/i)) {
            e.preventDefault();
            setUrl(decodeURI(e.target.dataset.url));
            q("#form").requestSubmit();
        }
    });

    //Act on hash changes
    window.addEventListener("hashchange", function (e) {
        const url = location.hash.substring(1);
        console.log("hash change. New URL:", url);
        setUrl(url);
        q("#form").requestSubmit();
    });

    //If there's already a gemini URL set, use it immediately.
    if ((location?.hash ?? "").length > 1) {
        setUrl(location.hash.substring(1));
        q("#form").requestSubmit();
    }

    //Back button
    q("#btnBack").addEventListener("click", function () {
        history.back();
    });
})(document.querySelector.bind(document), document.querySelectorAll.bind(document), document.createElement.bind(document));