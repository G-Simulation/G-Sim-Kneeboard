var FORMULAS_DEBUG = (typeof DEBUG_CONFIG !== 'undefined' && DEBUG_CONFIG.FORMULAS) || false;

// Zentraler Logger - nutzt KneeboardLogger falls verfügbar
var formulasLogger = (typeof KneeboardLogger !== 'undefined')
	? KneeboardLogger.createLogger('Formulas', { minLevel: FORMULAS_DEBUG ? 'DEBUG' : 'INFO' })
	: { info: function(){}, warn: console.warn.bind(console), error: console.error.bind(console) };

// Utility-Funktion für Zahlenprüfung (ersetzt String.prototype.isNumber)
function isNumberString(value) {
	if (typeof value !== 'string') return false;
	return /^-?[\d.]+(?:e-?\d+)?$/.test(value);
}

var keyboardActive = false;
var scroled = false;
var calc;
var style = 1;
var colorLight = "#cccccc";
var colorDark = "#333333";
var id2 = '';
var saveVal; // Debounce timer für saveValues

	// Export id2 getter/setter und clearFormula für Keyboard Reset
	window.setId2 = function(val) { id2 = val; };
	window.getId2 = function() { return id2; };

	function initFormulasPage() {
		const savedLight = localStorage.getItem("colorLight");
		const savedDark = localStorage.getItem("colorDark");
		const savedFontLight = localStorage.getItem("fontColorLight");
		const savedFontDark = localStorage.getItem("fontColorDark");

		if (savedLight && savedDark && savedFontLight && savedFontDark) {
			colorLight = savedLight;
			colorDark = savedDark;
			fontColorLight = savedFontLight;
			fontColorDark = savedFontDark;
			document.documentElement.style.setProperty('--light', colorLight);
			document.documentElement.style.setProperty('--dark', colorDark);
			document.documentElement.style.setProperty('--fontLight', fontColorLight);
			document.documentElement.style.setProperty('--fontDark', fontColorDark);
			FORMULAS_DEBUG && console.log("🎨 Farben aus Cache übernommen:", colorLight, colorDark);
		}

		window.addEventListener("message", handleIncomingMessage);

		const formulasContainer = document.getElementById("Formulas");
		if (!formulasContainer) {
			console.warn("[Formulas] Container element not found, aborting init.");
			return;
		}
		if (typeof $ !== "function") {
			console.error("[Formulas] jQuery missing, cannot bind inputs.");
			return;
		}
		const $formulas = $(formulasContainer);

		$formulas.find('textarea').parent('td').on('click', function () {
			var textarea = $(this).find('textarea:first-child');
			if (!textarea.length) {
				return;
			}
			var el = textarea.get(0);
			textarea.focus();
			setCaretToPos(textarea, -1);
			window.Keyboard.open(el.value, function (currentValue) {
				el.value = currentValue;
				var inputEvt;
				try {
					inputEvt = new InputEvent('input', { bubbles: true, data: currentValue, inputType: 'insertText' });
				} catch (err) {
					inputEvt = new Event('input', { bubbles: true });
				}
				el.dispatchEvent(inputEvt);
			}, el);
		});

		$formulas.find('textarea').parent('td').on('dblclick', function () {
			var textarea = $(this).find('textarea:first-child');
			if (!textarea.length) {
				return;
			}
			var el = textarea.get(0);
			textarea.focus();
			textarea.select();
			window.Keyboard.open(el.value, function (currentValue) {
				el.value = currentValue;
				var inputEvt;
				try {
					inputEvt = new InputEvent('input', { bubbles: true, data: currentValue, inputType: 'insertText' });
				} catch (err) {
					inputEvt = new Event('input', { bubbles: true });
				}
				el.dispatchEvent(inputEvt);
			}, el);
		});

		$formulas.find('input, textarea').on('input', function () {
			id2 = this.id; // merkt sich das aktive Feld
			calculate();
		});

		$formulas.find('input, textarea').on('change', function () {
			id2 = this.id;
			calculate();
		});
	};

	// Formulas calculation
	// Flight Time

	function calculate() {
		if (!document.getElementById("Formulas")) {
			return;
		}
		if (typeof document.getElementById !== 'function') {
			return;
		}
		const ensureValue = (id) => {
			const el = document.getElementById(id);
			return el && typeof el.value === 'string' ? el.value : '';
		};
		const assignValue = (id, value) => {
			const el = document.getElementById(id);
			if (el) {
				el.value = value;
			}
		};
		const numeric = (value) => (typeof value === 'string' ? value.replace(/[^0-9.-]+/g, "") : "");
		if (!id2 || typeof id2 !== 'string') {
			return;
		}
		if (!document.getElementById(id2)) {
			return;
		}
		if (id2 !== '') {
			var FtDistance = numeric(ensureValue('FtDistance'));
			var FtGroundSpeed = numeric(ensureValue('FtGroundSpeed'));
			var Ft = numeric(ensureValue('COFT'));

			if (FtDistance != null) {
				if (isNumberString(FtDistance) && id2 == 'FtDistance') {
					if (isNumberString(FtGroundSpeed)) {
						assignValue('COFT', ((FtDistance / FtGroundSpeed) * 60).toFixed(0));
					}
					assignValue('FtDistance', FtDistance + ' nm');
				}
			}

			if (FtGroundSpeed != null) {
				if (isNumberString(FtGroundSpeed) && id2 == 'FtGroundSpeed') {
					if (isNumberString(FtDistance)) {
						assignValue('COFT', ((FtDistance / FtGroundSpeed) * 60).toFixed(0));
					}
					assignValue('FtGroundSpeed', FtGroundSpeed + ' kts');
				}
			}

			// Initiating Distance
			var IDDOTFH = document.getElementById('IDDOTFH').value.replace(/[^0-9.-]+/g, "");
			var ID = document.getElementById('ID').value.replace(/[^0-9.-]+/g, "");

			if (IDDOTFH != null) {
				if (isNumberString(IDDOTFH) && id2 == 'IDDOTFH') {
					document.getElementById('ID').value = ((IDDOTFH / 1000) * 3).toFixed(2);
					document.getElementById('IDDOTFH').value = IDDOTFH + ' feet';
				}
			}

			// Standard rate one turn
			var SROTTSS = document.getElementById('SROTTSS').value.replace(/[^0-9.-]+/g, "");
			var SROT = document.getElementById('SROT').value.replace(/[^0-9.-]+/g, "");

			if (SROTTSS != null) {
				if (isNumberString(SROTTSS) && id2 == 'SROTTSS') {
					document.getElementById('SROT').value = (SROTTSS / 10 + 7).toFixed(0);
					document.getElementById('SROTTSS').value = SROTTSS + ' kts';
				}
			}

			// Reject curve
			var RCBA = document.getElementById('RCBA').value.replace(/[^0-9.-]+/g, "");
			var RC = document.getElementById('RC').value.replace(/[^0-9.-]+/g, "");

			if (RCBA != null) {
				if (isNumberString(RCBA) && id2 == 'RCBA') {
					document.getElementById('RC').value = (RCBA / 2).toFixed(1);
					document.getElementById('RCBA').value = RCBA + ' °'
				}
			}

			// mb in in.hg
			var CMBINMB = document.getElementById('CMBINMB').value.replace(/[^0-9.-]+/g, "");
			var CMBINHG = document.getElementById('CMBINHG').value.replace(/[^0-9.-]+/g, "");

			if (CMBINMB != null) {
				if (isNumberString(CMBINMB) && id2 == 'CMBINMB') {
					document.getElementById('CMBINHG').value = (CMBINMB / 33.856).toFixed(2);
					document.getElementById('CMBINMB').value = CMBINMB + ' mb';
				}
			}

			if (CMBINHG != null) {
				if (isNumberString(CMBINHG) && id2 == 'CMBINHG') {
					document.getElementById('CMBINMB').value = (CMBINHG * 33.856).toFixed(0) + ' mb';
					document.getElementById('CMBINHG').value = (CMBINHG).toFixed(2);
				}
			}

			// Rate of Descent
			var SRCGS = document.getElementById('SRCGS').value.replace(/[^0-9.-]+/g, "");
			var CTIDEG = document.getElementById('CTIDEG').value.replace(/[^0-9.-]+/g, "");
			var ROD = document.getElementById('ROD').value.replace(/[^0-9.-]+/g, "");

			if (isNumberString(SRCGS) && id2 == 'SRCGS') {
				if (isNumberString(CTIDEG)) {
					document.getElementById('ROD').value = (SRCGS * CTIDEG * 1.77).toFixed(0);
				}
				document.getElementById('SRCGS').value = SRCGS + ' KTS';
			}

			if (isNumberString(CTIDEG) && id2 == 'CTIDEG') {
				if (isNumberString(SRCGS)) {
					document.getElementById('ROD').value = (SRCGS * CTIDEG * 1.77).toFixed(0);
				}
				document.getElementById('CTIDEG').value = CTIDEG + ' °';
			}


			// IAS in TAS
			var CTIIAS = document.getElementById('CTIIAS').value.replace(/[^0-9.-]+/g, "");
			var CTIIAS2 = document.getElementById('CTIIAS2').value.replace(/[^0-9.-]+/g, "");
			var CTIHIF = document.getElementById('CTIHIF').value.replace(/[^0-9.-]+/g, "");

			if (isNumberString(CTIIAS) && id2 == 'CTIIAS') {
				if (isNumberString(CTIHIF)) {
					document.getElementById('CTI').value = (CTIIAS * (1 + (CTIHIF / 1000 * .02))).toFixed(0);
				}
				document.getElementById('CTIIAS').value = CTIIAS + ' KIAS';
				document.getElementById('CTIIAS2').value = CTIIAS + ' KIAS';
			}

			if (isNumberString(CTIIAS2) && id2 == 'CTIIAS2') {
				if (isNumberString(CTIHIF)) {
					document.getElementById('CTI').value = (CTIIAS * (1 + (CTIHIF / 1000 * .02))).toFixed(0);
				}
				document.getElementById('CTIIAS').value = CTIIAS2 + ' KIAS';
				document.getElementById('CTIIAS2').value = CTIIAS2 + ' KIAS';
			}

			if (isNumberString(CTIHIF) && id2 == 'CTIHIF') {
				if (isNumberString(CTIIAS)) {
					document.getElementById('CTI').value = (CTIIAS * (1 + (CTIHIF / 1000 * .02))).toFixed(0);
				}
				document.getElementById('CTIHIF').value = CTIHIF + ' ft';
			}

			// Head wind component
			var HWCWD = document.getElementById('HWCWD').value.replace(/[^0-9.-]+/g, "");
			var HWCC = document.getElementById('HWCC').value.replace(/[^0-9.-]+/g, "");
			var HWCWS = document.getElementById('HWCWS').value.replace(/[^0-9.-]+/g, "");
			var HWC = document.getElementById('HWC').value.replace(/[^0-9.-]+/g, "");

			if (HWCWD != null) {
				if (isNumberString(HWCWD) && id2 == 'HWCWD') {
					if (isNumberString(HWCC) && isNumberString(HWCWS)) {
						document.getElementById('HWC').value = (Math.cos(HWCWD - HWCC) * HWCWS).toFixed(2);
					}
					document.getElementById('HWCWD').value = '( ' + HWCWD + ' °';
				}
			}

			if (HWCC != null) {
				if (isNumberString(HWCC) && id2 == 'HWCC') {
					if (isNumberString(HWCWD) && isNumberString(HWCWS)) {
						document.getElementById('HWC').value = (Math.cos(HWCWD - HWCC) * HWCWS).toFixed(2);
					}
					document.getElementById('HWCC').value = HWCC + ' ° )';
				}
			}

			if (HWCWS != null) {
				if (isNumberString(HWCWS) && id2 == 'HWCWS') {
					if (isNumberString(HWCC) && isNumberString(HWCWD)) {
						document.getElementById('HWC').value = (Math.cos(HWCWD - HWCC) * HWCWS).toFixed(2);
					}
					document.getElementById('HWCWS').value = HWCWS + ' kts';
				}
			}

			// Side wind component
			var SWCWD = document.getElementById('SWCWD').value.replace(/[^0-9.-]+/g, "");
			var SWCC = document.getElementById('SWCC').value.replace(/[^0-9.-]+/g, "");
			var SWCWS = document.getElementById('SWCWS').value.replace(/[^0-9.-]+/g, "");
			var SWC = document.getElementById('SWC').value.replace(/[^0-9.-]+/g, "");

			if (SWCWD != null) {
				if (isNumberString(SWCWD) && id2 == 'SWCWD') {
					if (isNumberString(SWCC) && isNumberString(SWCWS)) {
						document.getElementById('SWC').value = (Math.sin(SWCWD - SWCC) * SWCWS).toFixed(2);
					}
					document.getElementById('SWCWD').value = '( ' + SWCWD + ' °';
				}
			}

			if (SWCC != null) {
				if (isNumberString(SWCC) && id2 == 'SWCC') {
					if (isNumberString(SWCWD) && isNumberString(SWCWS)) {
						document.getElementById('SWC').value = (Math.sin(SWCWD - SWCC) * SWCWS).toFixed(2);
					}
					document.getElementById('SWCC').value = SWCC + ' ° )';
				}
			}

			if (SWCWS != null) {
				if (isNumberString(SWCWS) && id2 == 'SWCWS') {
					if (isNumberString(SWCWD) && isNumberString(SWCC)) {
						document.getElementById('SWC').value = (Math.sin(SWCWD - SWCC) * SWCWS).toFixed(2);
					}
					document.getElementById('SWCWS').value = SWCWS + ' kts';
				}
			}

			// Wind correction angle
			var WCACC = document.getElementById('WCACC').value.replace(/[^0-9.-]+/g, "");
			var WCATSS = document.getElementById('WCATSS').value.replace(/[^0-9.-]+/g, "");
			var WCA = document.getElementById('WCA').value.replace(/[^0-9.-]+/g, "");

			if (WCACC != null) {
				if (isNumberString(WCACC) && id2 == 'WCACC') {
					if (isNumberString(WCATSS)) {
						document.getElementById('WCA').value = ((WCACC * 60) / WCATSS).toFixed(0);
					}
					document.getElementById('WCACC').value = WCACC + ' kts';
				}
			}

			if (WCATSS != null) {
				if (isNumberString(WCATSS) && id2 == 'WCATSS') {
					if (isNumberString(WCACC)) {
						document.getElementById('WCA').value = ((WCACC * 60) / WCATSS).toFixed(0);
					}
					document.getElementById('WCATSS').value = WCATSS + ' kts';
				}
			}
			// save values
			clearTimeout(saveVal);
			saveVal = setTimeout(saveValues, 400);
		}
	}

	function clearFormula() {
		// Bei Reset alle Felder zurücksetzen, auch wenn id2 leer war
		if (id2 != '' || id2 === 'Reset') {
			// Helper function für sichere Wertzuweisung
			const setVal = (elId, val) => {
				const el = document.getElementById(elId);
				if (el) el.value = val;
			};

			if (id2 == 'FtDistance' || id2 == 'Reset') {
				setVal('FtDistance', 'Distance (nm)');
				setVal('COFT', '');
			}

			if (id2 == 'FtGroundSpeed' || id2 == 'Reset') {
				setVal('FtGroundSpeed', 'Ground speed (kts)');
				setVal('COFT', '');
			}

			if (id2 == 'IDDOTFH' || id2 == 'Reset') {
				setVal('IDDOTFH', 'Height difference (ft)');
				setVal('ID', '');
			}

			if (id2 == 'SROTTSS' || id2 == 'Reset') {
				setVal('SROTTSS', 'True self speed (kts)');
				setVal('SROT', '');
			}

			if (id2 == 'RCBA' || id2 == 'Reset') {
				setVal('RCBA', 'Bank angle (°)');
				setVal('RC', '');
			}

			if (id2 == 'CMBINMB' || id2 == 'Reset') {
				setVal('CMBINMB', 'Millibar (mb)');
				setVal('CMBINHG', '');
			}

			if (id2 == 'CMBINHG' || id2 == 'Reset') {
				setVal('CMBINHG', '');
			}

			if (id2 == 'CTIIAS' || id2 == 'Reset') {
				setVal('CTIIAS', 'IAS (kts)');
				setVal('CTIIAS2', 'IAS (kts)');
				setVal('CTI', '');
			}

			if (id2 == 'CTIIAS2' || id2 == 'Reset') {
				setVal('CTIIAS', 'IAS (kts)');
				setVal('CTIIAS2', 'IAS (kts)');
				setVal('CTI', '');
			}

			if (id2 == 'CTIHIF' || id2 == 'Reset') {
				setVal('CTIHIF', 'Height (ft)');
				setVal('CTI', '');
			}

			if (id2 == 'HWCWD' || id2 == 'Reset') {
				setVal('HWCWD', '( Wind dir. (°)');
				setVal('HWC', '');
			}

			if (id2 == 'HWCC' || id2 == 'Reset') {
				setVal('HWCC', 'Course (°) )');
				setVal('HWC', '');
			}

			if (id2 == 'HWCWS' || id2 == 'Reset') {
				setVal('HWCWS', 'Wind speed (kts)');
				setVal('HWC', '');
			}

			if (id2 == 'SWCWD' || id2 == 'Reset') {
				setVal('SWCWD', '( Wind dir. (°)');
				setVal('SWC', '');
			}

			if (id2 == 'SWCC' || id2 == 'Reset') {
				setVal('SWCC', 'Course (°) )');
				setVal('SWC', '');
			}

			if (id2 == 'SWCWS' || id2 == 'Reset') {
				setVal('SWCWS', 'Wind speed (kts)');
				setVal('SWC', '');
			}

			if (id2 == 'WCACC' || id2 == 'Reset') {
				setVal('WCACC', 'Crosswind component (kts)');
				setVal('WCA', '');
			}

			if (id2 == 'WCATSS' || id2 == 'Reset') {
				setVal('WCATSS', 'True self speed (kts)');
				setVal('WCA', '');
			}

			if (id2 == 'SRCGS' || id2 == 'Reset') {
				setVal('SRCGS', 'GS (kts)');
				setVal('ROD', '');
			}

			if (id2 == 'CTIDEG' || id2 == 'Reset') {
				setVal('CTIDEG', 'DEG (Grad)');
				setVal('ROD', '');
			}


			// save values
			clearTimeout(saveVal);
			saveVal = setTimeout(saveValues, 400);
		}
	}

	// Export clearFormula für Keyboard Reset
	window.clearFormula = clearFormula;

	// Speichert Formulas-Werte im localStorage (debounced)
	function saveValues() {
		var formulaIds = [
			"FtDistance", "FtGroundSpeed", "COFT", "IDDOTFH", "ID",
			"SROTTSS", "SROT", "RCBA", "RC", "CMBINMB", "CMBINHG",
			"SRCGS", "CTIDEG", "ROD", "CTIIAS", "CTIIAS2", "CTIHIF", "CTI",
			"HWCWD", "HWCC", "HWCWS", "HWC", "SWCWD", "SWCC", "SWCWS", "SWC",
			"WCACC", "WCATSS", "WCA"
		];
		try {
			formulaIds.forEach(function(id) {
				var el = document.getElementById(id);
				if (el && el.value) {
					localStorage.setItem("formulas_" + id, el.value);
				}
			});
		} catch (err) {
			formulasLogger.warn("Could not save formulas to localStorage:", err);
		}
	}

	function updateFormulasColors(light, dark) {
		colorLight = light;
		colorDark = dark;
		if (recolor === "A") myColor = colorLight;
		ctx.strokeStyle = myColor;
	}

	function handleIncomingMessage(e) {
		if (typeof e.data === "string" && e.data.startsWith("colors(")) {
			const colorString = e.data.slice(7, -1);
			const [light, dark, fontLight, fontDark] = colorString.split("_");
			colorLight = light.trim();
			colorDark  = dark.trim(); 
			fontColorLight = fontLight.trim();
			fontColorDark = fontDark.trim();
			// Theme übernehmen
			document.documentElement.style.setProperty('--light', colorLight);
			document.documentElement.style.setProperty('--dark', colorDark);
			document.documentElement.style.setProperty('--fontLight', fontColorLight);
			document.documentElement.style.setProperty('--fontDark', fontColorDark);
			// 🟡 Farben dauerhaft speichern
			localStorage.setItem("colorLight", colorLight);
			localStorage.setItem("colorDark", colorDark);
			localStorage.setItem("fontColorLight", fontColorLight);
			localStorage.setItem("fontColorDark", fontColorDark);
			FORMULAS_DEBUG && console.log("💾 Farben aktualisiert & gespeichert:", colorLight, colorDark, fontColorLight, fontColorDark);
		}
	}	
