"use strict";

(function () {
    const dlg = new ezDlg(true);
    async function showLargeMosaic(key) {
        const img = document.createElement("img");
        img.src = mosaic(key, 40, true);
        img.alt = img.title = "Mosaic for " + key;
        await dlg.alert([
            "The image below is a unique visual representation of the identity. Identical images refer to the same identity.",
            img
        ], img.title);
    }

    function evtShowLargeMosaic(e) {
        e.preventDefault();
        showLargeMosaic(this.dataset.key);
    }

    window.mosaic = function mosaic(data, factor, rawData) {
        if (factor === void 0) {
            factor = 1;
        }
        if (rawData === void 0) {
            rawData = false;
        }
        if (typeof (rawData) !== typeof (false)) {
            throw "RawData not a boolean";
        }
        if (typeof (factor) !== typeof (0)) {
            throw "Factor is not a number";
        }
        if (factor != factor | 0) {
            throw "Factor is not an integer";
        }
        if (factor < 1) {
            throw "Factor must be 1 or bigger";
        }
        if (typeof (data) !== typeof ("")) {
            throw "data is not a string";
        }
        if (data.length === 0) {
            throw "data cannot be an empty string";
        }
        if (!data.match(/^(?:[a-fA-F\d]{2})+$/)) {
            throw "data must be a hexadecimal string";
        }

        //These are the 16 colors that the hex 0-F maps to
        //You can change them but this will of course render the mosaic incompatible with existing ones.
        //The order of the colors has been chosen to basically match 16 color CGA.
        const pal = [
            "000000", //Black
            "000080", //Blue
            "008000", //Green
            "008080", //Aqua
            "800000", //Red
            "800080", //Purple
            "808000", //Yellow
            "C0C0C0", //White

            "808080", //B-Black
            "0000FF", //B-Blue
            "00FF00", //B-Green
            "00FFFF", //B-Cyan
            "FF0000", //B-Red
            "FF00FF", //B-Purple
            "FFFF00", //B-Yellow
            "FFFFFF", //B-White
        ];

        const w = Math.ceil(Math.sqrt(data.length));
        const h = Math.ceil(data.length / w);
        const canvas = document.createElement("canvas");
        canvas.width = w * factor;
        canvas.height = h * factor;
        const ctx = canvas.getContext("2d");
        const colors = data.split("").map(v => parseInt(v, 16));

        //Color rectangles
        for (let i = 0; i < data.length; i++) {
            const x = i % w;
            const y = (i / w) | 0;
            ctx.strokeStyle = ctx.fillStyle = "#" + pal[colors[i]];
            ctx.fillRect(x * factor, y * factor, factor, factor);
        }
        //Mark extra pixels as invalid
        for (let i = data.length; i < w * h; i++) {
            const x = i % w;
            const y = (i / w) | 0;
            ctx.strokeStyle = "#FF0000";
            ctx.moveTo(x * factor, y * factor);
            ctx.lineTo((x + 1) * factor, (y + 1) * factor);
            ctx.moveTo(x * factor, (y + 1) * factor);
            ctx.lineTo((x + 1) * factor, y * factor);
            ctx.stroke();
        }
        ctx.translate(0.5, 0.5);
        //Border
        ctx.strokeStyle = "#000000";
        ctx.strokeRect(0, 0, w * factor - 1, h * factor - 1);
        if (rawData) {
            return canvas.toDataURL();
        }
        const img = document.createElement("img");
        img.src = canvas.toDataURL();
        img.alt = img.title = "Mosaic for " + data;
        img.dataset.key = data;
        img.addEventListener("click", evtShowLargeMosaic);
        img.classList.add("mosaic-image", "img-thumbnail");
        return img;
    };
})();