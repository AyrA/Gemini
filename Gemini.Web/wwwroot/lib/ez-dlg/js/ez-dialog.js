"use strict";

//Initialize dialog handler
function ezDlg(closeOnBackdrop) {
	const DLG_ID = "ezdialog";

	//Boolean conversion
	closeOnBackdrop = !!closeOnBackdrop;

	//Check for null or undefined
	const nou = function (x) {
		return x === null || x === void 0;
	};

	//Converts text or DOM values into a JS array
	const toNodeArray = function (x) {
		//Do not attempt to modify arrays or empty values
		if (nou(x) || x instanceof Array) {
			return x;
		}
		if (x instanceof Node) {
			return [x];
		}
		if (x instanceof NodeList) {
			return Array.from(x);
		}
		return [Object.assign(document.createElement("p"), {
				textContent: x
			})];
	};

	//Create a dialog element and set HTML content values
	const mkDlg = function (body, title, buttons) {
		const dialog = document.createElement("dialog");
		let addClickHandler = closeOnBackdrop;
		dialog.classList.add("ez-dialog");
		dialog.id = DLG_ID + ezDlg.index++;
		//Basic dialog layout for all dialog windows
		dialog.innerHTML = "<form method=\"dialog\"><div></div><menu></menu></form>";
		//Add title element if a title is present
		if (!nou(title) && title.length > 0) {
			dialog.insertAdjacentHTML("afterbegin", "<h1></h1>");
			dialog.querySelector("h1").textContent = title;
		}
		//Add body content if present
		if (!nou(body)) {
			var content = dialog.querySelector("div");
			//Insert body values
			toNodeArray(body).forEach(function (v) {
				if (v instanceof Node) {
					content.appendChild(v);
				} else {
					content.appendChild(document.createElement("p")).textContent = v;
				}
			});
		}
		//Add buttons at the bottom
		if (buttons instanceof Array && buttons.length > 0) {
			const container = dialog.querySelector("menu");
			buttons.forEach(function (btnInfo) {
				if (nou(btnInfo) || typeof(btnInfo) !== typeof({})) {
					console.error("Invalid button info:", btnInfo);
					throw new Error("Button info not a valid object");
				}
				if (typeof(btnInfo.value) !== typeof("")) {
					throw new Error("Button info lacks a value property or it's not a string.");
				}
				const btn = document.createElement("button");
				btn.textContent = btnInfo.text || btnInfo.value;
				btn.value = btnInfo.value;
                if (btnInfo.isCancel) {
                    //Disable form validation when the button is used
                    btn.addEventListener("click", function () {
                        dialog.close();
                    });
					btn.dataset.isCancel = "true";
				}
				container.appendChild(btn);
				if (!Object.isFrozen(btnInfo)) {
					btnInfo.buttonElement = btn;
				}
			});
		} else {
			addClickHandler = true;
		}
		if (addClickHandler) {
			//Attach a click handler to the dialog if no button is present
			//The handler closes the dialog if the backdrop is clicked
			dialog.addEventListener("click", function (e) {
				const rect = {
					top: this.offsetTop,
					left: this.offsetLeft,
					width: this.clientWidth,
					height: this.clientHeight,
					right: this.offsetLeft + this.clientWidth,
					bottom: this.offsetTop + this.clientHeight
				}
				if (e.clientX < rect.left || e.clientX > rect.right || e.clientY < rect.top || e.clientY > rect.bottom) {
					dialog.close();
				}
			});
		}
		document.body.appendChild(dialog);
		return dialog;
	};

	//Show a dialog and return the appropriate button value
	const showDlg = async function (dialog) {
		return new Promise(function (a, r) {
			//Dialog button clicked
			const fSuccess = function () {
				dialog.removeEventListener("close", fSuccess);
				dialog.removeEventListener("cancel", fCancel);
				dialog.remove();
				a(dialog.returnValue);
			};
			//Dialog cancelled (usually via ESC)
			const fCancel = function () {
				dialog.removeEventListener("close", fSuccess);
				dialog.removeEventListener("cancel", fCancel);
				dialog.remove();
				const buttons = dialog.querySelectorAll("menu button");
				for (var i = 0; i < buttons.length; i++) {
                    if (buttons[i].dataset.isCancel === "true") {
						return a(buttons[i].value);
					}
				}
				//Reject if no cancel button found
				r();
			};
			//Attach custom events and show modal dialog
			dialog.addEventListener("close", fSuccess);
			dialog.addEventListener("cancel", fCancel);
			dialog.showModal();
		});
	};

	//Construct object with all dialog functions
	const ret = {
		confirm: async function (body, title, okBtn, cancelBtn) {
			const btn = [{
					value: okBtn || "OK"
				}, {
					value: cancelBtn || "Cancel",
					isCancel: true
				}
			];
			const dlg = mkDlg(body, title, btn);
			const result = await showDlg(dlg);
			return result === btn[0].value;
		},
		alert: function (body, title, okBtn) {
			const btn = [{
					value: okBtn || "OK",
					isCancel: true
				}
			];
			const dlg = mkDlg(body, title, btn);
			return showDlg(dlg);
		},
		prompt: async function (body, prefill, title, okBtn, cancelBtn) {
			const btn = [{
					value: okBtn || "OK"
				}, {
					value: cancelBtn || "Cancel",
					isCancel: true
				}
			];
			const textbox = document.createElement("input");
			const html = (toNodeArray(body) || []).concat(textbox);
			textbox.value = prefill || "";
			const dlg = mkDlg(html, title, btn);
			const result = await showDlg(dlg);
			return result === btn[0].value ? textbox.value : null;
		},
		custom: function (body, title, buttons) {
			if (nou(buttons)) {
				buttons = [{
						value: "OK",
						isCancel: true
					}
				];
			}
			const dlg = mkDlg(body, title, buttons);
			return showDlg(dlg);
		}
	};
	//Return final object
	return ret;
}
ezDlg.index = 0;
ezDlg.version = "0.9";
ezDlg.isSupported = function () {
	//Require HTML dialog element support
	if (typeof(window.HTMLDialogElement) !== typeof(ezDlg)) {
		return false;
	}
	//Require showModal function
	if (typeof(window.HTMLDialogElement.prototype.showModal) !== typeof(ezDlg)) {
		return false;
	}
	return true;
};
