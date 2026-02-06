var DOCUMENTS_DEBUG = (typeof DEBUG_CONFIG !== 'undefined' && DEBUG_CONFIG.DOCUMENTS) || false;

// Zentraler Logger - nutzt KneeboardLogger falls verfügbar
var docsLogger = (typeof KneeboardLogger !== 'undefined')
	? KneeboardLogger.createLogger('Documents', { minLevel: DOCUMENTS_DEBUG ? 'DEBUG' : 'INFO' })
	: { info: function(){}, warn: console.warn.bind(console), error: console.error.bind(console) };

// Sichere localStorage Wrapper
function safeGetItem(key) {
	try { return localStorage.getItem(key); }
	catch (e) { docsLogger.warn("localStorage.getItem failed:", key, e); return null; }
}
function safeSetItem(key, value) {
	try { localStorage.setItem(key, value); return true; }
	catch (e) { docsLogger.warn("localStorage.setItem failed:", key, e); return false; }
}

var documents = 0;
var myState = {
	currentDoc: 1,
	totalDoc: 0,
	currentImg: 1,
	currentName: 0,
	totalImg: 0,
	angle: 0,
	eframeState: 0,
	zoom: 1
};
var folders = [];
var foldersClosed = [];
(function restoreClosedFolders() {
	try {
		var stored = safeGetItem("foldersClosed");
		if (stored) {
			var parsed = JSON.parse(stored);
			if (Array.isArray(parsed)) {
				foldersClosed = parsed;
			}
		}
	} catch (err) {
		docsLogger.warn("konnte foldersClosed nicht laden:", err);
	}
})();

var currentDocName;
var documentList;
var list;
var filesLoaded = false;
var documentEntries = [];


// --- Globale Variablen ---
let pageNum = 1;
let canvasDoc = null;
let pendingPageAfterLoad = 1;
let currentDocumentEntry = null;
let currentDocumentSource = null;
let imageMessageElement = null;
let activeImageRequestId = 0;

const zoomStep = 0.25;
const minZoom = 0.25;
const maxZoom = 5.0;
const SMALL_CANVAS_TOP_SPACING = 24; // px fallback gap
const TOP_GAP_RATIO = 0.01;          // 1vh equivalent
const MIN_TOP_GAP_PX = 6;
const DOC_STATE_VERSION = "doc-state-v5";
const IMAGE_EXTENSION = ".png";
const IMAGE_COUNT_CACHE_PREFIX = "docImageCount-v4:";
const IMAGE_COUNT_TIMESTAMP_PREFIX = "docImageCountTS-v4:";
const MAX_PAGE_SCAN = 800;
const CACHE_VALIDITY_MS = 5 * 60 * 1000; // 5 Minuten
const CACHE_RECHECK_THRESHOLD = 10; // Bei weniger als 10 Seiten öfter neu prüfen
let headRequestSupported = true;


// --- Native Seitengröße (wird dynamisch via PDF gesetzt) ---
let nativePageWidth = 1;
let nativePageHeight = 1;

let zoomLevelDoc = 1.0;           // Nutzer-Zoom (1 = Container füllen)
let baseScale = 1.0;           // Maßstab, um die Seite in den Container einzupassen
let shouldCenterAfterRender = true;
let resizeHandlerRegistered = false;
let canvasFitState = {
	widthDiff: 0,
	heightDiff: 0,
	topGap: SMALL_CANVAS_TOP_SPACING,
	canPanX: false,
	canPanY: false
};


function showSpinner(isVisible) {
	const spinner = document.getElementById('spinner');
	const noDocsLabel = document.getElementById('noDocumentsLabel');
	const canvas = document.getElementById('eimg');
	if (!spinner) {
		return;
	}
	if (isVisible) {
		spinner.style.visibility = "visible";
		spinner.style.display = "block";
		if (noDocsLabel) {
			noDocsLabel.style.display = "none";
		}
		if (canvas) {
			canvas.style.visibility = "hidden";
		}
	} else {
		spinner.style.visibility = "hidden";
		spinner.style.display = "none";
		if (noDocsLabel) {
			noDocsLabel.style.display = folders.length ? "none" : "block";
		}
		if (canvas) {
			canvas.style.visibility = "visible";
		}
	}
}

function updateDocIndicator() {
	const docInput = document.getElementById('current_doc');
	const noDocsLabel = document.getElementById('noDocumentsLabel');
	const totalDocs = documents || folders.length || myState.totalDoc || 0;
	if (!docInput) {
		return;
	}
	if (!totalDocs) {
		docInput.value = "0/0";
		if (noDocsLabel) {
			noDocsLabel.style.display = "block";
		}
		return;
	}
	if (noDocsLabel) {
		noDocsLabel.style.display = "none";
	}
	const currentDocNumber = Math.min(Math.max(actDoc, 1), totalDocs);
	docInput.value = `${currentDocNumber}/${totalDocs}`;
}

function updatePageIndicatorDisplay() {
	const pageInput = document.getElementById('current_page');
	if (!pageInput) {
		return;
	}
	if (!myState.totalImg) {
		pageInput.value = "0/0";
		return;
	}
	const currentPage = Math.min(Math.max(myState.currentImg, 1), myState.totalImg);
	pageInput.value = `${currentPage}/${myState.totalImg}`;
}

function notifyParentAboutState() {
	if (typeof postMessage === "function") {
		try {
			postMessage(actDoc + '_' + documents + '_' + myState.currentImg + '_' + myState.totalImg);
		} catch (err) {
			// Silent fail
		}
	}
}

function goToPageNumber(targetPage) {
	if (!currentDocumentSource || !currentDocumentSource.pageCount) {
		updatePageIndicatorDisplay();
		return;
	}
	const totalPages = currentDocumentSource.pageCount || myState.totalImg || 1;
	const numericTarget = parseInt(targetPage, 10);
	if (!numericTarget || numericTarget < 1) {
		updatePageIndicatorDisplay();
		return;
	}
	const clamped = Math.min(Math.max(numericTarget, 1), totalPages);
	if (clamped === pageNum) {
		updatePageIndicatorDisplay();
		return;
	}
	pageNum = clamped;
	myState.currentImg = clamped;
	localStorage.setItem("currentImg", clamped);
	updatePageIndicatorDisplay();
	shouldCenterAfterRender = true;
	displayCurrentPageImage();
	notifyParentAboutState();
}

function loadDocumentByIndex(targetIndex, targetPage = 1) {
	if (!folders || !folders.length) {
		return;
	}
	const safeIndex = clampValue(targetIndex, 0, folders.length - 1);
	myState.currentDoc = safeIndex;
	actDoc = safeIndex + 1;
	const entry = documentEntries[safeIndex] || null;
	currentDocumentEntry = entry;
	myState.currentName = entry ? entry.serverName : (folders[safeIndex] || '');
	const normalizedPage = parseInt(targetPage, 10);
	const safePage = (!isNaN(normalizedPage) && normalizedPage > 0) ? normalizedPage : 1;
	myState.currentImg = safePage;
	pendingPageAfterLoad = safePage;
	currentDocumentSource = null;
	localStorage.setItem("currentDoc", safeIndex);
	localStorage.setItem("currentImg", safePage);
	updateDocIndicator();
	updatePageIndicatorDisplay();
	setListItem();
	loadImagesForCurrentDocument();
}

function getNativePageWidth() {
	return Math.max(nativePageWidth, 1);
}

function getNativePageHeight() {
	return Math.max(nativePageHeight, 1);
}

function updateNativePageSize(width, height) {
	if (!isFinite(width) || !isFinite(height)) {
		return;
	}
	if (width <= 0 || height <= 0) {
		return;
	}
	nativePageWidth = width;
	nativePageHeight = height;
}

function initDocumentsPage() {
	autoHideDocumentsOverlay();
	showSpinner(true);
	if (localStorage.getItem("portSave") != null) {
		port = Number(localStorage.getItem("portSave"));
		document.getElementById('portInput').value = port;
	}
	src = '';

	// URL-Parameter auslesen für direkten Dokumenten-Zugriff
	var urlParams = new URLSearchParams(window.location.search);
	var docParam = urlParams.get('doc');
	var pageParam = urlParams.get('page');
	if (docParam !== null) {
		var docIndex = parseInt(docParam, 10);
		var pageIndex = pageParam !== null ? parseInt(pageParam, 10) : 1;
		if (!isNaN(docIndex) && docIndex > 0) {
			// Speichere die gewünschten Werte, werden nach dem Laden der Dokumentenliste verwendet
			localStorage.setItem("pendingDocIndex", docIndex - 1); // 0-basiert
			if (!isNaN(pageIndex) && pageIndex > 0) {
				localStorage.setItem("pendingPageIndex", pageIndex);
			}
		}
	}

	//getFiles();
	canvasDoc = document.getElementById("eimg");
	imageMessageElement = document.getElementById("imageMessage");
	if (canvasDoc) {
		canvasDoc.draggable = false;
		canvasDoc.style.visibility = "hidden";
	}
	const ele = document.getElementById('container');
	ele.style.cursor = 'grab';
	let pos = { top: 0, left: 0, x: 0, y: 0 };
	const mouseDownHandler = function (e) {
		const hasScrollableContent = (ele.scrollWidth > ele.clientWidth) || (ele.scrollHeight > ele.clientHeight);
		if (!hasScrollableContent) {
			return;
		}
		ele.style.cursor = 'grabbing';
		ele.style.userSelect = 'none';
		pos = {
			left: ele.scrollLeft,
			top: ele.scrollTop,
			x: e.clientX,
			y: e.clientY,
			canScrollX: ele.scrollWidth > ele.clientWidth,
			canScrollY: ele.scrollHeight > ele.clientHeight
		};
		document.addEventListener('mousemove', mouseMoveHandler);
		document.addEventListener('mouseup', mouseUpHandler);
	};
	const mouseMoveHandler = function (e) {
		// How far the mouse has been moved
		const dx = e.clientX - pos.x;
		const dy = e.clientY - pos.y;
		// Scroll the element
		if (pos.canScrollY) {
			ele.scrollTop = pos.top - dy;
		}

		if (pos.canScrollX) {
			ele.scrollLeft = pos.left - dx;
		}
	};
	const mouseUpHandler = function () {
		const hasScrollableContent = (ele.scrollWidth > ele.clientWidth) || (ele.scrollHeight > ele.clientHeight);
		ele.style.cursor = hasScrollableContent ? 'grab' : 'default';
		ele.style.removeProperty('user-select');
		document.removeEventListener('mousemove', mouseMoveHandler);
		document.removeEventListener('mouseup', mouseUpHandler);
	};
	// Attach the handler
	ele.addEventListener('mousedown', mouseDownHandler);
	if (!resizeHandlerRegistered) {
		window.addEventListener('resize', fitCanvasToWindow);
		resizeHandlerRegistered = true;
	}
	// Get the message reference
	var messageElement = document.getElementById('message');
	// Register the event listener
	window.addEventListener('message', receiveMessage);

	const image = document.getElementById('eimg');
	if (image) {
		image.onscrollforeward = function (e) {
			ezoom(+1, 0);
		};

		image.onscrollbackward = function (e) {
			ezoom(-1, 0);
		};

		image.onwheel = function _image__onwheel(e) {
			e.preventDefault();
			if (e.deltaY < 0) this.onscrollforeward(e);
			else this.onscrollbackward(e);
		};

		image.addEventListener('dblclick', function (e) {
			e.preventDefault();
			resetZoom();
		});
	}

	document.getElementById('current_page').addEventListener('keypress', (e) => {
		// Get key code
		var code = (e.keyCode ? e.keyCode : e.which);
		// If key code matches that of the Enter key
		if (code == 13) {
			var rawValue = document.getElementById('current_page').value;
			var desiredPage = parseInt(rawValue, 10);
			if (!isNaN(desiredPage)) {
				go_page(desiredPage);
			} else {
				updatePageIndicatorDisplay();
			}
		}
	});

	// Hardware keyboard Enter für current_doc
	document.getElementById('current_doc').addEventListener('keypress', (e) => {
		var code = (e.keyCode ? e.keyCode : e.which);
		if (code == 13) {
			var rawValue = document.getElementById('current_doc').value;
			var desiredDoc = parseInt(rawValue, 10);
			if (!isNaN(desiredDoc)) {
				go_doc(desiredDoc);
			} else {
				updateDocIndicator();
			}
		}
	});

	// Virtuelle Tastatur und MSFS Focus für current_page
	var currentPageInput = document.getElementById('current_page');
	currentPageInput.addEventListener('click', function() {
		// Wert leeren bei Klick
		this.value = '';
		// MSFS Coherent GT: Signalisiere Input-Focus
		if (typeof engine !== 'undefined' && engine.trigger) {
			try {
				engine.trigger('FOCUS_INPUT_FIELD');
			} catch(e) {}
		}
		// Virtuelle Tastatur öffnen
		if (window.Keyboard && typeof window.Keyboard.open === 'function') {
			window.Keyboard.open(
				'',
				(currentValue) => { this.value = currentValue; },
				this
			);
		}
	});
	currentPageInput.addEventListener('blur', function() {
		if (typeof engine !== 'undefined' && engine.trigger) {
			try {
				engine.trigger('UNFOCUS_INPUT_FIELD');
			} catch(e) {}
		}
	});

	// Virtuelle Tastatur und MSFS Focus für current_doc
	var currentDocInput = document.getElementById('current_doc');
	currentDocInput.addEventListener('click', function() {
		// Wert leeren bei Klick
		this.value = '';
		if (typeof engine !== 'undefined' && engine.trigger) {
			try {
				engine.trigger('FOCUS_INPUT_FIELD');
			} catch(e) {}
		}
		if (window.Keyboard && typeof window.Keyboard.open === 'function') {
			window.Keyboard.open(
				'',
				(currentValue) => { this.value = currentValue; },
				this
			);
		}
	});
	currentDocInput.addEventListener('blur', function() {
		if (typeof engine !== 'undefined' && engine.trigger) {
			try {
				engine.trigger('UNFOCUS_INPUT_FIELD');
			} catch(e) {}
		}
	});

	getDocumentsList();
}

function showImageMessage(message) {
	if (!imageMessageElement) {
		imageMessageElement = document.getElementById("imageMessage");
	}
	if (!imageMessageElement) {
		return;
	}
	if (message) {
		imageMessageElement.textContent = message;
		imageMessageElement.classList.add("visible");
	} else {
		imageMessageElement.textContent = "";
		imageMessageElement.classList.remove("visible");
	}
}

function hideImageMessage() {
	showImageMessage("");
}

function sanitizeDocumentSegment(value) {
	var safe = (value || "").replace(/[^0-9A-Za-z]+/g, "_");
	if (!safe) {
		return "doc";
	}
	return safe;
}

function createDocumentEntry(fullName) {
	var normalized = (fullName || "").trim();
	var hasExplicitPath = normalized.indexOf("/") !== -1 || normalized.indexOf("\\") !== -1;
	var pathSegments = normalized.split(/[\\/]/);
	var serverName = pathSegments[pathSegments.length - 1] || normalized;
	var folderSegment = pathSegments.length > 1 ? pathSegments.slice(0, -1).join("/") : "";
	var sanitizedFolder = folderSegment ? sanitizeDocumentSegment(folderSegment) : "";
	var sanitizedFile = sanitizeDocumentSegment(serverName);
	var imageDirectory = "";
	if (hasExplicitPath) {
		// User provided an explicit directory under /images (e.g. "images/Kneeboard_manual_1_8").
		var normalizedPath = normalized.replace(/\\/g, "/").replace(/^\.?\/*/, "");
		if (normalizedPath.toLowerCase().indexOf("images/") === 0) {
			imageDirectory = normalizedPath;
		} else {
			imageDirectory = "images/" + normalizedPath;
		}
	} else {
		var directoryParts = ["images"];
		if (sanitizedFolder) {
			directoryParts.push(sanitizedFolder);
		}
		directoryParts.push(sanitizedFile);
		imageDirectory = directoryParts.join("/");
	}
	return {
		fullName: normalized,
		serverName: serverName,
		folderSegment: folderSegment,
		folderSanitized: sanitizedFolder,
		fileSanitized: sanitizedFile,
		imageDirectory: imageDirectory,
		storageKey: (folderSegment ? folderSegment + "::" : "") + serverName
	};
}

function buildImageUrl(meta, pageNumber) {
	var safePage = clampValue(parseInt(pageNumber, 10) || 1, 1, MAX_PAGE_SCAN);
	var directory = (meta && meta.imageDirectory) ? meta.imageDirectory : "images";
	var fileBase = (meta && meta.fileSanitized) ? meta.fileSanitized : sanitizeDocumentSegment(myState.currentName || "page");
	return "./" + directory + "/" + fileBase + "_" + safePage + IMAGE_EXTENSION;
}

function appendCacheBuster(url) {
	var separator = url.indexOf("?") >= 0 ? "&" : "?";
	return url + separator + "cb=" + Date.now();
}

function doesImageExist(url) {
	var probeUrl = appendCacheBuster(url);
	return new Promise(function (resolve) {
		// Try HEAD request first, fall back to GET if HEAD is not supported
		fetch(probeUrl, { method: 'HEAD' })
			.then(function (response) {
				if (response.ok) {
					resolve(true);
				} else if (response.status === 405) {
					// Method Not Allowed - try GET instead
					return fetch(probeUrl, { method: 'GET' }).then(function(r) {
						resolve(r.ok);
					});
				} else {
					resolve(false);
				}
			})
			.catch(function () {
				// If HEAD fails, try GET
				fetch(probeUrl, { method: 'GET' })
					.then(function(response) {
						resolve(response.ok);
					})
					.catch(function() {
						resolve(false);
					});
			});
	});
}

async function probeImageCount(meta) {
	var builder = function (page) { return buildImageUrl(meta, page); };
	if (!(await doesImageExist(builder(1)))) {
		return 0;
	}
	var low = 1;
	var high = 2;
	while (high <= MAX_PAGE_SCAN && (await doesImageExist(builder(high)))) {
		low = high;
		high *= 2;
	}
	var left = low + 1;
	var right = Math.min(high - 1, MAX_PAGE_SCAN);
	var lastExisting = low;
	while (left <= right) {
		var mid = Math.floor((left + right) / 2);
		if (await doesImageExist(builder(mid))) {
			lastExisting = mid;
			left = mid + 1;
		} else {
			right = mid - 1;
		}
	}
	return lastExisting;
}

function determineImageCount(meta) {
	var cacheKey = IMAGE_COUNT_CACHE_PREFIX + (meta.storageKey || meta.fileSanitized || meta.serverName || "");
	var timestampKey = IMAGE_COUNT_TIMESTAMP_PREFIX + (meta.storageKey || meta.fileSanitized || meta.serverName || "");
	var cachedValue = parseInt(localStorage.getItem(cacheKey), 10);
	var cachedTimestamp = parseInt(localStorage.getItem(timestampKey), 10);
	var now = Date.now();

	// Prüfe ob Cache gültig ist
	var cacheIsValid = false;
	if (!isNaN(cachedValue) && cachedValue > 0 && !isNaN(cachedTimestamp)) {
		var cacheAge = now - cachedTimestamp;

		// Bei weniger als CACHE_RECHECK_THRESHOLD Seiten: Cache nur 30 Sekunden gültig
		// (wahrscheinlich werden noch Bilder erstellt)
		if (cachedValue < CACHE_RECHECK_THRESHOLD) {
			cacheIsValid = cacheAge < 30000; // 30 Sekunden
			if (!cacheIsValid) {
				DOCUMENTS_DEBUG && console.log("Cache expired for " + meta.serverName + " (small page count, re-checking for new pages)");
			}
		} else {
			// Bei vielen Seiten: Cache 5 Minuten gültig
			cacheIsValid = cacheAge < CACHE_VALIDITY_MS;
			if (!cacheIsValid) {
				DOCUMENTS_DEBUG && console.log("Cache expired for " + meta.serverName + " (older than 5 minutes)");
			}
		}
	}

	if (cacheIsValid) {
		DOCUMENTS_DEBUG && console.log("Using cached page count for " + meta.serverName + ": " + cachedValue);
		return Promise.resolve(cachedValue);
	}

	DOCUMENTS_DEBUG && console.log("Probing image count for " + meta.serverName + "...");
	return probeImageCount(meta).then(function (count) {
		DOCUMENTS_DEBUG && console.log("Found " + count + " pages for " + meta.serverName);
		if (count > 0) {
			localStorage.setItem(cacheKey, String(count));
			localStorage.setItem(timestampKey, String(now));
		}
		return count;
	});
}

function resolveImageSource(entry) {
	return determineImageCount(entry).then(function (count) {
		return {
			entry: entry,
			pageCount: count,
			getImageUrl: function (page) {
				return buildImageUrl(entry, page);
			}
		};
	});
}

// Hilfsfunktion zum manuellen Aktualisieren der Seitenzahl
function refreshCurrentDocumentPageCount() {
	if (!currentDocumentEntry) {
		console.warn("Kein Dokument geladen");
		return;
	}
	var cacheKey = IMAGE_COUNT_CACHE_PREFIX + (currentDocumentEntry.storageKey || currentDocumentEntry.fileSanitized || currentDocumentEntry.serverName || "");
	var timestampKey = IMAGE_COUNT_TIMESTAMP_PREFIX + (currentDocumentEntry.storageKey || currentDocumentEntry.fileSanitized || currentDocumentEntry.serverName || "");

	// Lösche Cache
	localStorage.removeItem(cacheKey);
	localStorage.removeItem(timestampKey);

	DOCUMENTS_DEBUG && console.log("Cache für " + currentDocumentEntry.serverName + " gelöscht. Lade Dokument neu...");

	// Lade Dokument neu
	loadImagesForCurrentDocument();
}

// Mache die Funktion global verfügbar für die Konsole
window.refreshPageCount = refreshCurrentDocumentPageCount;

function clearCurrentImageElement() {
	if (!canvasDoc) {
		return;
	}
	canvasDoc.removeAttribute("src");
	canvasDoc.style.visibility = "hidden";
}

function loadImagesForCurrentDocument() {
	if (!currentDocumentEntry) {
		showSpinner(false);
		showImageMessage("Dokument nicht gefunden.");
		return;
	}
	clearCurrentImageElement();
	showSpinner(true);
	showImageMessage("Dokument wird geladen…");
	resolveImageSource(currentDocumentEntry).then(function (source) {
		if (!source || !source.pageCount) {
			currentDocumentSource = null;
			myState.totalImg = 0;
			updatePageIndicatorDisplay();
			showSpinner(false);
			showImageMessage("Keine Seiten gefunden.");
			return;
		}
		currentDocumentSource = source;
		myState.totalImg = source.pageCount;
		const initialPage = clampValue(pendingPageAfterLoad || 1, 1, source.pageCount);
		pendingPageAfterLoad = initialPage;
		pageNum = initialPage;
		myState.currentImg = initialPage;
		localStorage.setItem("currentImg", initialPage);
		updatePageIndicatorDisplay();
		updateDocIndicator();
		shouldCenterAfterRender = true;
		loadDocState();
		hideImageMessage();
		displayCurrentPageImage();
	}).catch(function (err) {
		currentDocumentSource = null;
		console.error("Fehler beim Laden der Dokumentbilder:", err);
		showSpinner(false);
		showImageMessage("Fehler beim Laden der Bilder.");
	});
}

function displayCurrentPageImage() {
	if (!canvasDoc || !currentDocumentSource || !currentDocumentSource.pageCount) {
		showSpinner(false);
		return;
	}
	const totalPages = currentDocumentSource.pageCount;
	const clamped = clampValue(pageNum, 1, totalPages);
	pageNum = clamped;
	myState.currentImg = clamped;
	localStorage.setItem("currentImg", clamped);
	updatePageIndicatorDisplay();
	const imageUrl = currentDocumentSource.getImageUrl(clamped);
	const requestId = ++activeImageRequestId;
	showSpinner(true);
	canvasDoc.style.visibility = "hidden";
	canvasDoc.onload = function () {
		if (requestId !== activeImageRequestId) {
			return;
		}
		updateNativePageSize(canvasDoc.naturalWidth || canvasDoc.width, canvasDoc.naturalHeight || canvasDoc.height);
		calculateBaseScale();
		applyImageSizing();
		if (shouldCenterAfterRender) {
			centerCanvasInContainer();
			shouldCenterAfterRender = false;
		}
		showSpinner(false);
		hideImageMessage();
		notifyParentAboutState();
	};
	canvasDoc.onerror = function () {
		if (requestId !== activeImageRequestId) {
			return;
		}
		showSpinner(false);
		showImageMessage("Seite konnte nicht geladen werden.");
	};
	canvasDoc.src = imageUrl;
}

function applyImageSizing() {
	if (!canvasDoc) {
		return;
	}
	const width = Math.max(Math.round(getNativePageWidth() * getCurrentScale()), 1);
	const height = Math.max(Math.round(getNativePageHeight() * getCurrentScale()), 1);
	canvasDoc.style.width = width + "px";
	canvasDoc.style.height = height + "px";
	canvasDoc.style.maxWidth = "none";
	canvasDoc.style.maxHeight = "none";
	updateZoomPadding();
	updateZoomIndicator();
}

function fitCanvasToWindow() {
	calculateBaseScale();
	if (!canvasDoc || !currentDocumentSource) {
		applyPanOffsets();
		return;
	}
	shouldCenterAfterRender = true;
	applyImageSizing();
	centerCanvasInContainer();
}

function calculateBaseScale() {
	const container = document.getElementById('container');
	const tabcontent = document.getElementById('Documents');
	if (!container) {
		return;
	}
	const toolbar = document.getElementById('toolbar');
	const availableWidth = tabcontent ? tabcontent.clientWidth : container.clientWidth;
	const viewportHeight = window.innerHeight || document.documentElement.clientHeight || container.clientHeight;
	if (!availableWidth || !viewportHeight) {
		return;
	}

	const toolbarGap = toolbar ? Math.max(toolbar.offsetHeight * 0.2, MIN_TOP_GAP_PX) : Math.max(viewportHeight * TOP_GAP_RATIO, MIN_TOP_GAP_PX);
	const effectiveWidth = Math.max(availableWidth, 0);
	const effectiveHeight = Math.max(viewportHeight - toolbarGap, 0);
	const nativeWidth = getNativePageWidth();
	const nativeHeight = getNativePageHeight();

	const widthScale = nativeWidth > 0 ? (effectiveWidth / nativeWidth) : null;
	const heightScale = (nativeHeight > 0 && effectiveHeight > 0) ? (effectiveHeight / nativeHeight) : null;

	let newScale = (widthScale !== null && isFinite(widthScale) && widthScale > 0) ? widthScale : null;

	if (heightScale !== null && isFinite(heightScale) && heightScale > 0) {
		if (newScale === null) {
			newScale = heightScale;
		} else {
			newScale = Math.min(newScale, heightScale);
		}
	}

	if (newScale !== null && isFinite(newScale) && newScale > 0) {
		baseScale = newScale;
	}
}

function getCurrentScale() {
	return Math.max(baseScale * zoomLevelDoc, 0.01);
}

function centerCanvasInContainer() {
	const container = document.getElementById('container');
	if (!container || !canvasDoc) {
		return;
	}

	const toolbar = document.getElementById('toolbar');
	const containerWidth = container.clientWidth;
	const containerHeight = container.clientHeight;
	const canvasWidth = canvasDoc.offsetWidth;
	const canvasHeight = canvasDoc.offsetHeight;

	const toolbarGap = toolbar ? Math.max(toolbar.offsetHeight * 0.2, MIN_TOP_GAP_PX) : Math.max(containerHeight * TOP_GAP_RATIO, MIN_TOP_GAP_PX);
	const baseTopGap = Math.max(toolbarGap, MIN_TOP_GAP_PX);
	const usableHeightWithBaseGap = Math.max(containerHeight - baseTopGap, 0);
	const heightDiff = usableHeightWithBaseGap - canvasHeight;
	const needsVerticalPan = heightDiff < 0;
	const appliedTopGap = needsVerticalPan ? SMALL_CANVAS_TOP_SPACING : baseTopGap;
	const usableHeight = Math.max(containerHeight - appliedTopGap, 0);

	const widthDiff = containerWidth - canvasWidth;

	canvasFitState.widthDiff = widthDiff;
	canvasFitState.heightDiff = usableHeight - canvasHeight;
	canvasFitState.topGap = appliedTopGap;
	canvasFitState.canPanX = canvasWidth > containerWidth;
	canvasFitState.canPanY = canvasHeight > containerHeight;

	const widthOverflow = Math.max(canvasWidth - containerWidth, 0);
	const verticalContentHeight = needsVerticalPan ? canvasHeight + appliedTopGap : canvasHeight;
	const heightOverflow = Math.max(verticalContentHeight - containerHeight, 0);

	container.scrollLeft = widthOverflow > 0 ? widthOverflow / 2 : 0;
	container.scrollTop = heightOverflow > 0 ? heightOverflow / 2 : 0;

	applyPanOffsets();
}

function applyPanOffsets() {
	if (!canvasDoc) {
		return;
	}
	const container = document.getElementById('container');
	if (!container) {
		return;
	}
	const containerWidth = container.clientWidth;
	const containerHeight = container.clientHeight;
	const canvasWidth = canvasDoc.offsetWidth;
	const canvasHeight = canvasDoc.offsetHeight;
	const topGap = Math.max(canvasFitState.topGap || SMALL_CANVAS_TOP_SPACING, SMALL_CANVAS_TOP_SPACING);
	const availableHeight = Math.max(containerHeight - topGap, 0);
	const extraWidth = Math.max(containerWidth - canvasWidth, 0);
	const extraHeight = Math.max(availableHeight - canvasHeight, 0);
	const needsVerticalPan = extraHeight < 0;

	if (extraWidth > 0) {
		canvasDoc.style.marginLeft = "auto";
		canvasDoc.style.marginRight = "auto";
	} else {
		canvasDoc.style.marginLeft = "0px";
		canvasDoc.style.marginRight = "0px";
	}

	const appliedTopGap = needsVerticalPan ? SMALL_CANVAS_TOP_SPACING : topGap;
	canvasDoc.style.marginTop = appliedTopGap + "px";
	if (needsVerticalPan) {
		canvasDoc.style.marginBottom = SMALL_CANVAS_TOP_SPACING + "px";
	} else {
		canvasDoc.style.marginBottom = extraHeight + "px";
	}

	const canPan = (canvasWidth > containerWidth) || (canvasHeight > containerHeight);
	container.style.cursor = canPan ? 'grab' : 'default';

}
	function onPrevPage() {
		if (pageNum <= 1) return;
		pageNum--;
		displayCurrentPageImage();
	}

	function addZeroBefore(n) {
		return (n < 10 ? '0' : '') + n;
	}

	function clampValue(value, min, max) {
		if (min > max) {
			const tmp = min;
			min = max;
			max = tmp;
		}
		return Math.min(Math.max(value, min), max);
	}

function clampZoomValue(value) {
	return clampValue(value, minZoom, maxZoom);
}

function parseStoredDocState(raw) {
	if (!raw || typeof raw !== "string") {
		return null;
	}
	var parts = raw.split("~");
	if (parts.length < 3) {
		return null;
	}
	var version = parts[2];
	if (version !== DOC_STATE_VERSION) {
		return null;
	}
	var zoomValue = parseFloat(parts[1]);
	if (!isFinite(zoomValue) || zoomValue <= 0) {
		zoomValue = 1;
	}
	return {
		angle: parts[0],
		zoom: zoomValue
	};
}

const ROTATION_CLASSNAMES = ["rotate0", "rotate90", "rotate180", "rotate270"];

function normalizeAngleValue(value) {
	var numeric = parseInt(value, 10);
	if (isNaN(numeric)) {
		return 0;
	}
	var normalized = ((numeric % 360) + 360) % 360;
	if (normalized === 90 || normalized === 180 || normalized === 270) {
		return normalized;
	}
	return 0;
}

function updateCanvasRotation(newAngle) {
	var appliedAngle = normalizeAngleValue(newAngle);
	angle = appliedAngle;
	['eimg', 'eframe'].forEach(function (id) {
		var element = document.getElementById(id);
		if (!element || !element.classList) {
			return;
		}
		ROTATION_CLASSNAMES.forEach(function (cls) {
			element.classList.remove(cls);
		});
		if (appliedAngle !== 0) {
			element.classList.add("rotate" + appliedAngle);
		}
	});
	return appliedAngle;
}

function updateZoomIndicator() {
	var zoomSpan = document.getElementById("zoomSpan");
	if (!zoomSpan) {
		return;
	}
		if (Math.abs(zoomLevelDoc - 1) < 0.001) {
			zoomSpan.style.visibility = "hidden";
		} else {
			zoomSpan.style.visibility = "visible";
			zoomSpan.innerText = `Zoom: ${(zoomLevelDoc * 100).toFixed(0)}%`;
		}
	}

function updateZoomPadding() {
	const spacer = document.getElementById("doc-scroll-spacer");
	if (!spacer) {
		return;
	}
	spacer.style.height = zoomLevelDoc > 0.501 ? "250px" : "0px";
}

function setZoomLevel(newZoom) {
	const clampedZoom = clampZoomValue(newZoom);
	const hasChanged = Math.abs(clampedZoom - zoomLevelDoc) >= 0.001;
	zoomLevelDoc = clampedZoom;
	myState.zoom = zoomLevelDoc;
	updateZoomIndicator();
	updateZoomPadding();
	if (!hasChanged) {
		return;
	}
	shouldCenterAfterRender = true;
	applyImageSizing();
	centerCanvasInContainer();
	saveDocState();
}

	function ezoom(direction, loaded) {
		if (loaded === 1) return;

		if (direction > 0) setZoomLevel(zoomLevelDoc + zoomStep);
		else if (direction < 0) setZoomLevel(zoomLevelDoc - zoomStep);
	}

	function resetZoom() {
		if (Math.abs(zoomLevelDoc - 1) < 0.001) {
			shouldCenterAfterRender = true;
			centerCanvasInContainer();
			return;
		}
		setZoomLevel(1.0);
	}

	function resetDoc() {
		localStorage.clear();
	}

	function rotate() {
		myState.angle = updateCanvasRotation(angle + 90);
		saveDocState();
		ezoom(0, 1);
		centerCanvasInContainer();
	}

	// Receive controls from parent page
	function receiveMessage(e) {
		if (browserDebug == 0) {
			var method_name = e.data;
			method = method_name.substring(0, method_name.lastIndexOf("("));
			arg = method_name.substring(method_name.lastIndexOf("(") + 1, method_name.lastIndexOf(")"));
			if (arg != '' && method != 'colors') {
				window[method](parseInt(arg));
			}
			else if (method == 'colors') {
				var colors = arg.split("_");
				document.documentElement.style.setProperty('--light', colors[0]);
				document.documentElement.style.setProperty('--dark', colors[1]);
			}
			else {
				window[method_name]();
			}
		}
	}

	function loadDocState() {
		var storedState = parseStoredDocState(localStorage.getItem(myState.currentName));
		var targetZoom = 1;
		if (storedState) {
			myState.angle = updateCanvasRotation(storedState.angle);
			myState.zoom = storedState.zoom;
			targetZoom = storedState.zoom;
		} else {
			myState.angle = updateCanvasRotation(0);
			myState.zoom = 1;
			saveDocState();
		}
		setZoomLevel(targetZoom);
	}

	function saveDocState() {
		if (!myState.currentName) {
			return;
		}
		try {
			var normalizedZoom = (isFinite(myState.zoom) && myState.zoom > 0) ? myState.zoom : 1;
			var payload = [myState.angle, normalizedZoom, DOC_STATE_VERSION].join("~");
			localStorage.setItem(myState.currentName, payload);
		} catch (err) {
			console.warn("Konnte Doc-State nicht speichern:", err);
		}
	}

var foldersList = [];
var documentsListLoading = false;
var nextDocumentsRefreshTime = 0;
const DOCUMENT_LIST_REFRESH_DELAY = 5000;
var autoOpenFirstDocumentPending = false;

function getDocumentsList(skipInitialLoad) {
	if (documentsListLoading) {
		return;
	}
	documentsListLoading = true;
	if (!skipInitialLoad) {
		autoHideDocumentsOverlay();
	}
    try {
        var xhr = new XMLHttpRequest();
        xhr.open("POST", 'getDocumentsList', true);
        var myText = "foobar";
        xhr.send(myText);

        xhr.onreadystatechange = function () {
            if (xhr.readyState == XMLHttpRequest.DONE) {
                documentsListLoading = false;
                nextDocumentsRefreshTime = Date.now() + DOCUMENT_LIST_REFRESH_DELAY;
                DOCUMENTS_DEBUG && console.log(xhr.responseText);

                list = xhr.responseText;

                // Originalnamen vom Server
                foldersList = [];
                folders = [];
                documentEntries = [];
                myState.totalDoc = 0;
                documents = 0;
                filesLoaded = false;

                // Server-String in einzelne Namen zerlegen
                let arrStr = list.split(/[""][""]/);
                arrStr.forEach(function (item) {
                    let filename = item.replace(/"/g, '').trim();
                    if (!filename) return;
                    foldersList.push(filename);   // z.B. "Total_line_lengths_ALPHA_8_DLS_26"
                });

                // UI leeren
                var _docListUl = document.querySelector('#docList > ul');
                if (_docListUl) _docListUl.innerHTML = "";
                var folderId = 0;
                var lastFolder = "";

                // Jede Datei einzeln rendern
                foldersList.forEach(function (fullName, index) {
                    var entryMeta = createDocumentEntry(fullName);
                    folders.push(entryMeta.serverName);
                    documentEntries.push(entryMeta);

                    // ID basierend auf Array-Index (1-basiert)
                    var docId = index + 1;

                    // "schöner" Anzeigename
                    var displayName = fullName.replace(/_/g, " ");
                    var parts = displayName.split("\\"); // Ordner\Datei?

                    if (parts.length > 1) {
                        var folderName = parts[0];
                        var fileName = parts[1];

                        // Ordnerüberschrift (nur einmal)
                        if (lastFolder !== folderName) {
                            var isClosed = foldersClosed.includes(folderName);
                            var folderStateClass = isClosed ? 'closed' : 'open';
                            if (_docListUl) _docListUl.insertAdjacentHTML("beforeend",
                                '<div id="folder_' + folderId + '" class="folder-header folder ' + folderStateClass + '" data-folder="' + folderName + '">' +
                                folderName +
                                '</div>'
                            );
                            lastFolder = folderName;
                            folderId++;
                        }

                        // Datei-Eintrag
                        if (!foldersClosed.includes(folderName)) {
                            if (_docListUl) _docListUl.insertAdjacentHTML("beforeend",
                                '<li id="' + docId + '" class="kneeboard-list-item target folder-item" data-folder="' + folderName + '">' +
                                '<i class="kneeboard-list-item-icon"></i>' +
                                '<div class="kneeboard-list-item-content">' +
                                '<span class="kneeboard-list-item-title">' + fileName + '</span>' +
                                '</div>' +
                                '</li>'
                            );
                        }
                    } else {
                        // Kein Ordner, nur Dateiname
				if (_docListUl) _docListUl.insertAdjacentHTML("beforeend",
							'<li id="' + docId + '" class="kneeboard-list-item target">' +
							'<i class="kneeboard-list-item-icon"></i>' +
							'<div class="kneeboard-list-item-content">' +
							'<span class="kneeboard-list-item-title">' + displayName + '</span>' +
							'</div>' +
							'</li>'
						);
				}

			});
				documents = folders.length;
				myState.totalDoc = folders.length;
				updateDocIndicator();
				filesLoaded = true;
				updateDocumentsOverlayVisibility();

				// Klick-Handler
				document.querySelectorAll(".target").forEach(function(el) {
					el.addEventListener("click", function(event) {
						event.preventDefault();
						document.querySelectorAll(".target").forEach(function(t) { t.style.color = "black"; });
						document.querySelectorAll(".folder").forEach(function(f) { f.style.color = "black"; });

						el.style.color = "red";

						var docNumber = parseInt(el.id, 10);
						if (!isNaN(docNumber)) {
							loadDocumentByIndex(docNumber - 1);
						}
					});
				});

                // Folder-Header Click Handler - Toggle Open/Closed
                document.querySelectorAll(".folder-header").forEach(function(fh) {
                    fh.addEventListener("click", function(event) {
                        event.preventDefault();
                        var folderLabel = fh.getAttribute("data-folder") || fh.textContent.trim();
                        if (!folderLabel) {
                            return;
                        }

                        // Toggle folder state
                        if (!foldersClosed.includes(folderLabel)) {
                            foldersClosed.push(folderLabel);
                            fh.classList.remove("open");
                            fh.classList.add("closed");
                        }
                        else {
                            foldersClosed = foldersClosed.filter(function(item) { return item !== folderLabel; });
                            fh.classList.remove("closed");
                            fh.classList.add("open");
                        }

                        localStorage.setItem("foldersClosed", JSON.stringify(foldersClosed));
                        getDocumentsList(true);
                    });
                });
				if (skipInitialLoad) {
                    if (autoOpenFirstDocumentPending) {
                        autoOpenFirstDocumentPending = false;
                        loadfirstDoc();
                    } else {
                        setListItem();
                    }
                } else {
                    autoOpenFirstDocumentPending = false;
                    loadfirstDoc();
                }
            }
        }
        xhr.onerror = function () {
            documentsListLoading = false;
            nextDocumentsRefreshTime = Date.now() + DOCUMENT_LIST_REFRESH_DELAY;
            console.warn("Documents: Liste konnte nicht geladen werden");
        };
    }
    catch (e) {
        console.error(e);
        documentsListLoading = false;
    }
	}

	var docListMinimized = false;
	var docOverlayAutoHidden = true;
	var docOverlayWatcherId = null;
	function minimizeDocList() {
		var docEl = document.getElementById("doc");
		var docList = document.getElementById("docList");
		var minimizeBtn = document.getElementById("docListMinimize");

		if (!docListMinimized) {
			// Minimieren
			if (docList) {
				docList.style.display = "none";
				docList.style.visibility = "hidden";
			}
			if (docEl) {
				docEl.style.height = "7vh";
				docEl.style.minHeight = "7vh";
			}
			if (minimizeBtn) minimizeBtn.innerHTML = "+";
			docListMinimized = true;
		} else {
			// Maximieren
			if (docList) {
				docList.style.display = "";
				docList.style.visibility = "visible";
			}
			if (docEl) {
				docEl.style.height = "50vh";
				docEl.style.minHeight = "";
			}
			if (minimizeBtn) minimizeBtn.innerHTML = "_";
			docListMinimized = false;
		}
	}

	function updateDocumentsOverlayVisibility() {
		var docElement = document.getElementById("doc");
		var noDocsLabel = document.getElementById("noDocumentsLabel");
		if (!docElement) {
			return;
		}

		var overlayHidden = docElement.style.visibility === 'hidden';

		if (!folders.length) {
			if (noDocsLabel) {
				noDocsLabel.style.display = "block";
			}
			maybeRefreshDocumentsList(false);
			if (!overlayHidden) {
				docElement.style.visibility = 'hidden';
				docOverlayAutoHidden = true;
			}
			return;
		}
		if (noDocsLabel) {
			noDocsLabel.style.display = "none";
		}

		if (docOverlayAutoHidden) {
			docElement.style.visibility = 'visible';
			var docList = document.getElementById("docList");
			if (docList) docList.style.display = "";
			docListMinimized = false;
			var docMinBtn = document.getElementById("docListMinimize");
			if (docMinBtn) docMinBtn.innerHTML = "_";
		}

		docOverlayAutoHidden = false;
	}

	function hidedocList() {
		wpListOn = false;
		docOverlayAutoHidden = false;
		document.getElementById("doc").style.visibility = 'hidden';
	}

	function autoHideDocumentsOverlay() {
		var docElement = document.getElementById("doc");
		if (!docElement) {
			return;
		}
		docElement.style.visibility = 'hidden';
		docOverlayAutoHidden = true;
	}

	function isSpinnerVisible() {
		var spinner = document.getElementById("spinner");
		if (!spinner) {
			return false;
		}
		var style = window.getComputedStyle ? window.getComputedStyle(spinner) : spinner.style;
		var displayValue = style && style.display !== undefined ? style.display : spinner.style.display;
		var visibilityValue = style && style.visibility !== undefined ? style.visibility : spinner.style.visibility;
		return displayValue !== 'none' && visibilityValue !== 'hidden';
	}

	function ensureDocOverlayWatcher() {
		if (docOverlayWatcherId !== null) {
			return;
		}
		docOverlayWatcherId = window.setInterval(checkDocOverlayReadiness, 1500);
	}

	function checkDocOverlayReadiness() {
		if (!docOverlayAutoHidden) {
			return;
		}
		if (!filesLoaded || !folders.length) {
			maybeRefreshDocumentsList(false);
			return;
		}
		if (isSpinnerVisible()) {
			return;
		}
		updateDocumentsOverlayVisibility();
	}

ensureDocOverlayWatcher();

function maybeRefreshDocumentsList(forceImmediate) {
	if (folders.length) {
		return;
	}
	if (documentsListLoading) {
		return;
	}
	var now = Date.now();
	if (!forceImmediate && now < nextDocumentsRefreshTime) {
		return;
	}
	nextDocumentsRefreshTime = now + DOCUMENT_LIST_REFRESH_DELAY;
	autoOpenFirstDocumentPending = true;
	getDocumentsList(true);
}

	function setListItem() {
		document.querySelectorAll(".target").forEach(function(el) { el.style.color = "black"; });
		document.querySelectorAll(".folder").forEach(function(el) { el.style.color = "black"; });
		if (document.getElementById(myState.currentDoc + 1)) {
			var ul = document.getElementById(myState.currentDoc + 1);
			ul.style.color = "red";
		}

	}

	function loadfirstDoc() {
		if (!filesLoaded || folders.length === 0) {
			return;
		}

		// Prüfe zuerst auf URL-Parameter (pendingDocIndex)
		var pendingDocIndex = localStorage.getItem("pendingDocIndex");
		var pendingPageIndex = localStorage.getItem("pendingPageIndex");

		var storedDocIndex = 0;
		var storedPage = 1;

		if (pendingDocIndex !== null) {
			// URL-Parameter verwenden
			storedDocIndex = parseInt(pendingDocIndex, 10);
			if (pendingPageIndex !== null) {
				storedPage = parseInt(pendingPageIndex, 10);
			}
			// Einmalig verwendete Parameter löschen
			localStorage.removeItem("pendingDocIndex");
			localStorage.removeItem("pendingPageIndex");
		} else {
			// Normale gespeicherte Werte verwenden
			storedDocIndex = parseInt(localStorage.getItem("currentDoc"), 10);
			storedPage = parseInt(localStorage.getItem("currentImg"), 10);
		}

		if (isNaN(storedDocIndex) || storedDocIndex < 0 || storedDocIndex >= folders.length) {
			storedDocIndex = 0;
		}
		if (isNaN(storedPage) || storedPage < 1) {
			storedPage = 1;
		}

		loadDocumentByIndex(storedDocIndex, storedPage);

		document.querySelectorAll("ul.menu li a").forEach(function(a) {
			a.addEventListener("click", function() {
				var tabclicked = a.getAttribute("href");
			});
		});

		var firstMenuLink = document.querySelector("ul.menu li:first-child a");
		if (firstMenuLink) firstMenuLink.click();
	}
	// Toolbar functions
	function changePort() {
		port = parseInt(document.getElementById('portInput').value);
		localStorage.setItem("portSave", String(port));
	}



	function go_previous_doc() {
		if (!filesLoaded || myState.currentDoc <= 0) {
			return;
		}
		loadDocumentByIndex(myState.currentDoc - 1, 1);
	}

	function docInput(value) {
		go_doc(value);
	}

	function go_doc(documentNumber) {
		if (!filesLoaded || !folders.length) {
			return;
		}
		var parsed = parseInt(documentNumber, 10);
		if (isNaN(parsed)) {
			updateDocIndicator();
			return;
		}
		var targetIndex = clampValue(parsed, 1, folders.length) - 1;
		loadDocumentByIndex(targetIndex, 1);
	}

	function go_next_doc() {
		if (!filesLoaded || myState.currentDoc >= folders.length - 1) {
			return;
		}
		loadDocumentByIndex(myState.currentDoc + 1, 1);
	}

	function go_previous() {
		if (!currentDocumentSource || pageNum <= 1) {
			return;
		}
		goToPageNumber(pageNum - 1);
	}

	function pageInput(value) {
		go_page(value);
	}

	function go_page(page) {
		goToPageNumber(page);
	}

	function go_next() {
		if (!currentDocumentSource || !currentDocumentSource.pageCount) {
			return;
		}
		var totalPages = currentDocumentSource.pageCount || myState.totalImg || 1;
		if (pageNum >= totalPages) {
			return;
		}
		goToPageNumber(pageNum + 1);
	}

	// ============================================================================
	// DOCUMENT LIST RESIZE FUNCTIONALITY
	// ============================================================================
	(function initDocResize() {
		var config = {
			element: null,
			handle: null,
			minWidth: 200,
			maxWidth: 600,
			isResizing: false,
			startX: 0,
			startWidth: 0
		};

		function onMouseMove(e) {
			if (!config.isResizing) return;

			e.preventDefault();

			var deltaX = e.clientX - config.startX;
			var newWidth = config.startWidth - deltaX;

			if (newWidth < config.minWidth) newWidth = config.minWidth;
			if (newWidth > config.maxWidth) newWidth = config.maxWidth;

			config.element.style.width = newWidth + 'px';
		}

		function onMouseEnd() {
			if (config.isResizing) {
				config.isResizing = false;
				document.body.style.cursor = '';
				document.body.style.userSelect = '';
			}
		}

		function onTouchMove(e) {
			if (!config.isResizing) return;

			e.preventDefault();

			var clientX = e.touches[0].clientX;
			var deltaX = clientX - config.startX;
			var newWidth = config.startWidth - deltaX;

			if (newWidth < config.minWidth) newWidth = config.minWidth;
			if (newWidth > config.maxWidth) newWidth = config.maxWidth;

			config.element.style.width = newWidth + 'px';
		}

		function onTouchEnd() {
			if (config.isResizing) {
				config.isResizing = false;
				document.body.style.cursor = '';
				document.body.style.userSelect = '';
			}

			// Remove touch listeners when done
			document.removeEventListener('touchmove', onTouchMove);
			document.removeEventListener('touchend', onTouchEnd);
		}

		function setupResizeHandle() {
			if (!config.element || !config.handle) return;

			function startMouseResize(e) {
				e.preventDefault();
				e.stopPropagation();

				config.isResizing = true;
				config.startX = e.clientX;
				config.startWidth = config.element.offsetWidth;

				document.body.style.cursor = 'ew-resize';
				document.body.style.userSelect = 'none';
			}

			function startTouchResize(e) {
				e.preventDefault();
				e.stopPropagation();

				config.isResizing = true;
				config.startX = e.touches[0].clientX;
				config.startWidth = config.element.offsetWidth;

				document.body.style.cursor = 'ew-resize';
				document.body.style.userSelect = 'none';

				// Only add touch listeners when starting a resize
				document.addEventListener('touchmove', onTouchMove, { passive: false });
				document.addEventListener('touchend', onTouchEnd);
			}

			// Mouse events
			config.handle.addEventListener('mousedown', startMouseResize);

			// Touch events for EFB
			config.handle.addEventListener('touchstart', startTouchResize, { passive: false });
		}

		function initializeResize() {
			config.element = document.getElementById('doc');
			if (config.element) {
				config.handle = config.element.querySelector('.resize-handle');
				if (config.handle) {
					setupResizeHandle();
				}
			}
		}

		// Globale Event Listener nur für Mouse (Touch wird dynamisch hinzugefügt/entfernt)
		document.addEventListener('mousemove', onMouseMove);
		document.addEventListener('mouseup', onMouseEnd);

		// Initialisierung bei DOM ready
		if (document.readyState === 'loading') {
			document.addEventListener('DOMContentLoaded', initializeResize);
		} else {
			initializeResize();
		}

		// MutationObserver für dynamisch geladene Elemente
		var observer = new MutationObserver(function() {
			if (!config.element || !config.handle) {
				initializeResize();
				if (config.element && config.handle) {
					observer.disconnect();
				}
			}
		});

		observer.observe(document.body, { childList: true, subtree: true });
	})();
