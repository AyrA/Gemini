"use strict";
(async function (q, qa, ce) {
    async function loadIdentities() {
        const response = await fetch("/Identity/CertificateList");
        const json = await response.json();
        console.log(json);
    }

    await loadIdentities();
})(document.querySelector.bind(document), document.querySelectorAll.bind(document), document.createElement.bind(document));
