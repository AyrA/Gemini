"use strict";
(async function (q, qa, ce) {
    const tbl = q("#tblCert tbody");
    const dlg = new ezDlg(false);
    async function loadIdentities() {
        const response = await fetch("/Identity/CertificateList");
        const json = await response.json();
        tbl.innerHTML = "";
        console.log(json);
        json.forEach(function (cert) {
            const row = tbl.appendChild(ce("tr"));
            row.dataset.certid = cert.id;

            const cells = {
                name: row.appendChild(ce("td")),
                id: row.appendChild(ce("td")),
                expires: row.appendChild(ce("td")),
                encrypted: row.appendChild(ce("td")),
                options: row.appendChild(ce("td"))
            };
            cells.name.textContent = cert.friendlyName;
            cells.id.title = cert.id;
            cells.id.textContent = cert.id.replace(/^(.{6})(.+)(.{6})$/, "$1...$3");
            cells.expires.textContent = new Date(cert.validUntil).toLocaleString();
            cells.encrypted.textContent = cert.encrypted ? "Yes" : "No";

            const buttons = {
                export: cells.options.appendChild(ce("button")),
                change: cells.options.appendChild(ce("button")),
                del: cells.options.appendChild(ce("button"))
            };

            //Styling
            buttons.del.classList.add("btn", "btn-danger", "m-1");
            buttons.change.classList.add("btn", "btn-warning", "m-1");
            buttons.export.classList.add("btn", "btn-success", "m-1");

            //Text
            buttons.del.textContent = "\uD83D\uDDD1\uFE0F";
            buttons.change.textContent = "\u270F\uFE0F";
            buttons.export.textContent = "\uD83D\uDCBE";

            //Helpers
            buttons.del.title = "Delete this certificate";
            buttons.change.title = "Update this certificate";
            buttons.export.title = "Download this certificate";

            //Events
            buttons.del.addEventListener("click", evtDeleteCert);
            buttons.change.addEventListener("click", evtChangeCert);
            buttons.export.addEventListener("click", evtExportCert);
        });

        //Add "new Cert" item
        const newRow = tbl.appendChild(ce("tr"));
        const newCell = newRow.appendChild(ce("td"));
        newCell.setAttribute("colspan", qa("#tblCert thead th").length);
        const newLink = newCell.appendChild(ce("a"));
        newLink.classList.add("btn", "btn-link", "d-block");
        newLink.addEventListener("click", evtAddCert);
        newLink.textContent = "Add new identity";
    }

    async function download(id) {
        const response = await fetch("/Identity/CertificateExport/" + id);
        if (response.ok) {
            const url = "data:text/plain;base64," + btoa(await response.text());
            const a = ce("a");
            a.href = url;
            a.download = id + ".pem";
            a.textContent = "Save Certificate";
            await dlg.custom(["Your certificate has been exported. Click the link below to download it", a],
                "Download complete",
                [{ value: "Close" }]);
        }
        else {
            await dlg.alert("Unable to download the certificate. Server reported " + response.status + " " + response.statusText, document.title);
        }
    }

    async function askDelete(id) {
        const dlgResult = await dlg.confirm([
            "Really delete the certificate " + id + "?",
            "This action cannot be undone. If you think you need the certificate again, consider exporting it first."],
            "Delete certificate", "Yes", "No");
        if (dlgResult) {
            const result = await fetch("/Identity/Certificate/" + id, { method: "DELETE" });
            if (result.ok) {
                await dlg.alert("Certificate " + id + " was deleted", "Certificate deleted");
                await loadIdentities();
            }
            else {
                await dlg.alert("Failed to delete certificate. " + await result.text(), "Certificate deleted");
            }
        }
    }

    function evtAddCert(e) { e.preventDefault(); }
    function evtDeleteCert(e) { e.preventDefault(); askDelete(this.parentNode.parentNode.dataset.certid); }
    function evtChangeCert(e) { e.preventDefault(); askChange(this.parentNode.parentNode.dataset.certid); }
    function evtExportCert(e) { e.preventDefault(); download(this.parentNode.parentNode.dataset.certid); }

    await loadIdentities();
})(document.querySelector.bind(document), document.querySelectorAll.bind(document), document.createElement.bind(document));
