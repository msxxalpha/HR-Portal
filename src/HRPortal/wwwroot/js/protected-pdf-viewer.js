(() => {
    const pdfModuleUrl = "https://cdnjs.cloudflare.com/ajax/libs/pdf.js/4.10.38/pdf.min.mjs";
    const pdfWorkerUrl = "https://cdnjs.cloudflare.com/ajax/libs/pdf.js/4.10.38/pdf.worker.min.mjs";

    const blockContextMenu = event => {
        event.preventDefault();
        event.stopPropagation();
    };

    document.addEventListener("contextmenu", blockContextMenu, true);
    document.addEventListener("keydown", event => {
        const key = (event.key || "").toLowerCase();
        if (((event.ctrlKey || event.metaKey) && (key === "s" || key === "p")) ||
            (event.metaKey && event.shiftKey && event.code === "KeyS")) {
            event.preventDefault();
            event.stopPropagation();
        }
    }, true);

    function showMessage(container, message) {
        const status = container.querySelector(".protected-pdf-status");
        if (status) {
            status.textContent = message;
            status.hidden = false;
        }
    }

    async function renderPdf(container, pdfjsLib) {
        const url = container.dataset.pdfUrl;
        const status = container.querySelector(".protected-pdf-status");
        const pages = container.querySelector(".protected-pdf-pages");
        if (!url || !pages) return;

        try {
            const response = await fetch(url, { credentials: "same-origin", cache: "no-store" });
            if (!response.ok) throw new Error("دریافت گزارش با خطا مواجه شد (HTTP " + response.status + ").");
            const bytes = new Uint8Array(await response.arrayBuffer());
            if (bytes.length < 5 || String.fromCharCode(...bytes.slice(0, 5)) !== "%PDF-") {
                throw new Error("گزارش با قالب PDF دریافت نشد. قالب گزارش فیش و حکم را روی PDF تنظیم کنید.");
            }

            const pdf = await pdfjsLib.getDocument({ data: bytes }).promise;
            if (status) status.hidden = true;

            for (let pageNumber = 1; pageNumber <= pdf.numPages; pageNumber++) {
                const page = await pdf.getPage(pageNumber);
                const baseViewport = page.getViewport({ scale: 1 });
                const availableWidth = Math.max(320, container.clientWidth - 28);
                const scale = Math.min(1.65, availableWidth / baseViewport.width);
                const viewport = page.getViewport({ scale });
                const outputScale = Math.min(window.devicePixelRatio || 1, 2);
                const wrapper = document.createElement("div");
                wrapper.className = "protected-pdf-page";
                const canvas = document.createElement("canvas");
                canvas.width = Math.floor(viewport.width * outputScale);
                canvas.height = Math.floor(viewport.height * outputScale);
                canvas.style.width = Math.floor(viewport.width) + "px";
                canvas.style.height = Math.floor(viewport.height) + "px";
                canvas.setAttribute("aria-label", "صفحه " + pageNumber + " از " + pdf.numPages);
                canvas.addEventListener("contextmenu", blockContextMenu, true);
                wrapper.appendChild(canvas);
                pages.appendChild(wrapper);
                const context = canvas.getContext("2d", { alpha: false });
                await page.render({
                    canvasContext: context,
                    viewport,
                    transform: outputScale === 1 ? null : [outputScale, 0, 0, outputScale, 0, 0]
                }).promise;
            }
        } catch (error) {
            showMessage(container, error?.message || "بارگذاری گزارش ناموفق بود.");
        }
    }

    async function init() {
        const containers = Array.from(document.querySelectorAll(".protected-pdf-viewer"));
        if (!containers.length) return;
        try {
            const pdfjsLib = await import(pdfModuleUrl);
            pdfjsLib.GlobalWorkerOptions.workerSrc = pdfWorkerUrl;
            for (const container of containers) await renderPdf(container, pdfjsLib);
        } catch {
            containers.forEach(container => showMessage(container, "نمایش امن PDF بارگذاری نشد. اتصال به سرویس نمایش گزارش را بررسی کنید."));
        }
    }

    if (document.readyState === "loading")
        document.addEventListener("DOMContentLoaded", init, { once: true });
    else
        init();
})();
