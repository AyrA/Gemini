"use strict";
(function (q, qa, ce) {
    const dlg = new ezDlg(false);

    window.selectIdentity = async function selectIdentity(id) {
        const response = await fetch("/Identity/CertificateList");
        if (response.ok) {
            const data = await response.json();
            if (data instanceof Array) {
                if (data.length === 0) {
                    const btn = [{
                        text: "Create an identity",
                        value: "create"
                    }, {
                        text: "Retry loading identities",
                        value: "retry"
                    }, {
                        isCancel: true,
                        text: "Cancel",
                        value: "cancel"
                    }];
                    const dlgResult = await dlg.custom("The remote server requests that you authenticate using one of your identities, but you don't yet have one.", "Select an identity", btn);
                    switch (dlgResult) {
                        case "retry":
                            return await selectIdentity(id);
                        case "create":
                            window.open("/Home/Identity");
                            return await selectIdentity(id);
                        case "cancel":
                            break;
                    }
                    return null;
                }
                const contents = [];
                const btn = [{
                    text: "OK",
                    value: "ok"
                }, {
                    isCancel: true,
                    text: "Cancel",
                    value: "cancel"
                }];

                //Title
                contents.push(ce("p"));
                contents[0].textContent = "Select an identity from the list below";

                //Identity selection
                contents.push(ce("select"));
                contents[1].name = "Identity";
                data.forEach(function (cert) {
                    const opt = contents[1].appendChild(ce("option"));
                    opt.value = cert.id;
                    if (cert.id === id) {
                        opt.setAttribute("selected", "selected");
                    }
                    opt.dataset.cert = JSON.stringify(cert);
                    opt.textContent = [
                        cert.name,
                        cert.id.replace(/^(.{6})(.+)(.{6})$/, "$1...$3"),
                        new Date(cert.validUntil).toLocaleDateString()
                    ].join(" - ");
                });

                //Mosaic image
                contents.push(ce("div"));
                contents[2].classList.add("mt-2");
                const img = contents[2].appendChild(ce("img"));
                img.src = mosaic(data[0].id, 6, true);

                //Password item
                contents.push(ce("div"));
                contents[3].appendChild(ce("p")).textContent = "This identity is password protected";
                const passField = contents[3].appendChild(ce("input"));
                passField.type = "password";
                passField.namd = "Password";
                passField.setAttribute("autocomplete", "off");
                passField.required = true;
                passField.placeholder = "Identity password";
                //Show/hide based on first certificate
                if (!data[0].encrypted) {
                    passField.disabled = true;
                    contents[3].classList.add("d-none");
                }

                //Change listener
                contents[1].addEventListener("change", function (e) {
                    e?.preventDefault();
                    const cert = JSON.parse(this.querySelector(`[value='${this.value}']`).dataset.cert);
                    img.src = mosaic(this.value, 6, true);
                    passField.disabled = !cert.encrypted;
                    if (cert.encrypted) {
                        contents[3].classList.remove("d-none");
                    }
                    else {
                        contents[3].classList.add("d-none");
                    }
                });

                //Render
                const selectResult = await dlg.custom(contents, "select an identity", btn);
                if (selectResult === "ok") {
                    return {
                        id: contents[1].value,
                        password: passField.disabled ? null : passField.value
                    };
                }
                return null;
            }
            console.error("API response is invalid.", data);
            throw new Error("API response is invalid.");
        }
        throw new Error(await response.text());
    };
})(document.querySelector.bind(document), document.querySelectorAll.bind(document), document.createElement.bind(document));
