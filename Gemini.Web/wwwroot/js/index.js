"use strict";
(async function (q, qa, ce) {
    const list = await (await fetch("/Identity/CertificateList")).json();
    if (list.length === 0) {
        q("#noId").classList.remove("d-none");
    }
})(document.querySelector.bind(document), document.querySelectorAll.bind(document), document.createElement.bind(document));
