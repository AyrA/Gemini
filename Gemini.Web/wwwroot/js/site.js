"use strict";
(function (q, qa, ce) {
    const dlg = new ezDlg(false);
    async function stop() {
        const dlgResult = await dlg.confirm("Really shutdown this application?", "Shutdown", "Shutdown", "Cancel");
        if (dlgResult) {
            const response = await fetch("/Service/Shutdown", { method: "POST" });
            if (response.ok) {
                document.body.innerHTML = "<h1>Application has been shut down</h1>";
                await dlg.alert("The application has been shut down. You can close your browser window now.", "Shutdown");
            }
            else {
                await dlg.alert("Call to the API failed. If this continues to happen, open the console window that runs this application and press CTRL+C to trigger a regular application shutdown.", "API failed");
            }
        }
    }

    q("#lnkShutdown").addEventListener("click", function (e) {
        e.preventDefault();
        stop();
    });
})(document.querySelector.bind(document), document.querySelectorAll.bind(document), document.createElement.bind(document));
