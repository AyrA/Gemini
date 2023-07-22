"use strict";
(async function (q, qa, ce) {
    const tbl = q("#tblCert tbody");
    const dlg = new ezDlg(false);

    async function loadIdentities() {
        const response = await fetch("/Identity/CertificateList");
        const json = await response.json();
        tbl.innerHTML = "";
        console.log(json);
        if (json.length > 0) {
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
                cells.name.textContent = cert.name;
                cells.id.title = cert.id;
                cells.id.appendChild(mosaic(cert.id, 6));
                cells.id.appendChild(ce("span")).textContent = cert.id.replace(/^(.{6})(.+)(.{6})$/, "$1...$3");
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
        }
        else {
            tbl.innerHTML = `<tr>
            <td colspan="${qa("#tblCert thead th").length}" class="text-center">
                <i>You don't have any identities yet.</i>
            </td>
            </tr>`;
        }

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
            "Delete certificate", "Delete", "Cancel");
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

    async function importCert() {
        const contents = Array.from(q("#importCertTemplate").content.childNodes).map(v => v.cloneNode(true));
        const btn = [{ text: "OK", value: "y" }, { text: "Cancel", value: "n", isCancel: true }];
        const dlgResult = await dlg.custom(contents, "Import an identity", btn);

        if (dlgResult === "y") {
            const div = ce("div");
            contents.forEach(v => div.appendChild(v));
            const file = div.querySelector("[name=Certificate]").files[0];
            const password = div.querySelector("[name=Password]").value;

            const fd = new FormData();
            fd.append("certificate", file);
            fd.append("password", password);
            const result = await fetch("/Identity/Certificate/", {
                method: "PUT",
                body: fd
            });
            if (result.ok) {
                await dlg.alert("Your identity was imported", "Identity imported");
                await loadIdentities();
            }
            else {
                await dlg.alert("The API failed with an error. " + (await result.text()), "API error");
            }
        }
    }

    async function createCert() {
        const contents = Array.from(q("#createCertTemplate").content.childNodes).map(v => v.cloneNode(true));
        const btn = [{ text: "OK", value: "y" }, { text: "Cancel", value: "n", isCancel: true }];
        const dlgResult = await dlg.custom(contents, "Create an identity", btn);

        if (dlgResult === "y") {
            const div = ce("div");
            contents.forEach(v => div.appendChild(v));
            const name = div.querySelector("[name=Name]").value;
            const password1 = div.querySelector("[name=Password1]").value;
            const password2 = div.querySelector("[name=Password2]").value;
            const exp = div.querySelector("[name=Expiration]").value;

            if (password1 != password2) {
                await dlg.alert("The passwords do not match. Please try again", "Invalid password");
                return await createCert();
            }

            const fd = new FormData();
            fd.append("displayName", name);
            if (password1.length > 0) {
                fd.append("password", password1);
            }
            fd.append("expiration", exp + "T00:00:00Z");
            const result = await fetch("/Identity/Certificate/", {
                method: "POST",
                body: fd
            });
            if (result.ok) {
                await dlg.alert("Your identity was created", "Identity created");
                await loadIdentities();
            }
            else {
                await dlg.alert("The API failed with an error. " + (await result.text()), "API error");
            }
        }
    }

    async function askAdd() {
        const result = await dlg.custom(
            "Do you want to create a new identity, or import an existing one?", "New identity",
            [
                { value: "create", text: "Create a new identity" },
                { value: "import", text: "Import an existing identity" },
                { isCancel: true, value: "cancel", text: "Cancel" }
            ]);
        if (result === "import") {
            await importCert();
        }
        else if (result === "create") {
            await createCert();
        }
    }

    function evtAddCert(e) { e.preventDefault(); askAdd(); }
    function evtDeleteCert(e) { e.preventDefault(); askDelete(this.parentNode.parentNode.dataset.certid); }
    function evtChangeCert(e) { e.preventDefault(); askChange(this.parentNode.parentNode.dataset.certid); }
    function evtExportCert(e) { e.preventDefault(); download(this.parentNode.parentNode.dataset.certid); }

    await loadIdentities();
})(document.querySelector.bind(document), document.querySelectorAll.bind(document), document.createElement.bind(document));
