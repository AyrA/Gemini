"use strict";
(function (q, qa, ce) {
    const dlg = new ezDlg(false);

    async function delItem(id, host) {
        const result = await dlg.confirm("Delete the certificate with id " + id + " for server " + host, "Delete certtificate", "Yes", "No");
        if (result) {
            const fd = new FormData();
            fd.append("id", id);
            const result = await fetch("/ServerTrust/Trust/" + encodeURIComponent(host), {
                method: "DELETE",
                body: fd
            });
            if (result.ok) {
                return await result.json();
            }
            return false;
        }
    }

    function formatDate(x) {
        if (typeof (x) === typeof ("")) {
            return formatDate(new Date(x));
        }
        if (x instanceof Date) {
            return x.toLocaleDateString() + " " + x.toLocaleTimeString();
        }
        throw "Invalid date argument";
    }

    function parseHost(host) {
        if (host.indexOf(":") < 0) {
            host += ":1965";
        }
        return host;
    }

    function readCert(file) {
        return new Promise(function (a, r) {
            const fr = new FileReader();
            fr.onload = function () {
                const m = fr.result.match(/[\-]+\s*BEGIN CERTIFICATE\s*[\-]+\s*([^\-]+)/);
                if (m) {
                    a(m[1].trim());
                }
                else {
                    //Not a Base64 certificate. Assume it's raw
                    fr.onload = function () {
                        a(fr.result.substr(fr.result.indexOf(',') + 1));
                    };
                    fr.readAsDataURL(file);
                }
            };
            fr.onabort = fr.onerror = r;
            fr.readAsBinaryString(file);
        });
    }

    async function addCert() {
        const contents = Array.from(q("#addCertTemplate").content.childNodes).map(v => v.cloneNode(true));
        const btn = [{ text: "OK", value: "y" }, { text: "Cancel", value: "n", isCancel: true }];
        const dlgResult = await dlg.custom(contents, "Add a certificate", btn);
        if (dlgResult === "y") {
            const div = ce("div");
            contents.forEach(v => div.appendChild(v));
            const host = parseHost(div.querySelector("[name=Host]").value);
            const cert = await readCert(div.querySelector("[name=Certificate]").files[0]);
            const fd = new FormData();
            fd.append("base64Cert", cert);
            const result = await fetch("/ServerTrust/Trust/" + encodeURIComponent(host), {
                method: "PUT",
                body: fd
            });
            if (result.ok) {
                await populateList();
            }
            else {
                await dlg.alert("The API failed with an error. " + (await result.text()), "API error");
            }
        }
    }

    function addCertEvent(e) {
        e.preventDefault();
        addCert();
    }

    function delItemEvent(e) {
        e.preventDefault();
        delItem(this.dataset.itemId, this.dataset.host).then(function (result) {
            if (result === void 0) {
                return;
            }
            if (result) {
                populateList();
            }
            else {
                dlg.alert("Failed to delete the specified certificate", document.title);
            }
        });
    }

    async function populateList() {
        const tbl = q("#tblTrust tbody");
        const response = await fetch("/ServerTrust/TrustList");

        tbl.innerHTML = "";
        if (response.ok) {
            const json = await response.json();
            if (json.length) {
                console.debug(json);
                json.forEach(function (entry) {
                    if (entry.publicKeys.length === 0) {
                        return;
                    }
                    entry.publicKeys.forEach(function (key) {
                        const row = tbl.appendChild(ce("tr"));
                        row.appendChild(ce("td")).textContent = entry.host.toLowerCase();
                        const idCell = row.appendChild(ce("td"));
                        idCell.appendChild(mosaic(key.id, 6));
                        idCell.appendChild(ce("span")).textContent = key.id.replace(/^(.{6})(.+)(.{6})$/, "$1...$3");

                        row.appendChild(ce("td")).textContent = formatDate(key.trustedAt);
                        row.appendChild(ce("td")).textContent = formatDate(key.trustExpires);
                        const opt = row.appendChild(ce("td"));
                        const dwnldBtn = opt.appendChild(ce("a"));
                        opt.appendChild(document.createTextNode(" "));
                        const delBtn = opt.appendChild(ce("button"));

                        dwnldBtn.classList.add("btn", "btn-primary");
                        dwnldBtn.setAttribute("download", entry.host.split(':')[0] + ".crt");
                        dwnldBtn.href = "data:application/pkcs8;base64," + key.certificate;
                        dwnldBtn.textContent = "\uD83D\uDCBE";

                        delBtn.classList.add("btn", "btn-danger");
                        delBtn.dataset.itemId = key.id;
                        delBtn.dataset.host = entry.host;
                        delBtn.textContent = "\u274C";
                        delBtn.addEventListener("click", delItemEvent);
                    });
                });
            }
        }
        else {
            await dlg.alert(await response.text(), document.title);
        }
        //Always add final row
        const addRow = tbl.appendChild(ce("tr"));
        const addCell = addRow.appendChild(ce("td"));
        addCell.setAttribute("colspan", 5);
        const addBtn = addCell.appendChild(ce("a"));
        addBtn.textContent = "Manually add a certificate";
        addBtn.classList.add("btn", "btn-link", "d-block", "text-center");
        addBtn.addEventListener("click", addCertEvent);
    }

    populateList();
})(document.querySelector.bind(document), document.querySelectorAll.bind(document), document.createElement.bind(document));
