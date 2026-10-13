// Affichage des PDF avec pdf.js embarqué (fonctionne aussi sur Chrome Android, qui n'affiche pas les PDF dans une iframe).
import * as pdfjsLib from "./lib/pdfjs/pdf.min.mjs";

pdfjsLib.GlobalWorkerOptions.workerSrc = new URL("./lib/pdfjs/pdf.worker.min.mjs", import.meta.url).href;

const tasks = new WeakMap();

export async function render(container, url, maxPages, pageLabel) {
    if (!container) return 0;
    const previous = tasks.get(container);
    if (previous) { try { await previous.destroy(); } catch { /* ignoré */ } }
    container.innerHTML = "";
    const absolute = new URL(url, document.baseURI).href;
    const task = pdfjsLib.getDocument({ url: absolute, isEvalSupported: false });
    tasks.set(container, task);
    const pdf = await task.promise;
    const count = Math.min(pdf.numPages, maxPages || 20);
    const ratio = window.devicePixelRatio || 1;
    for (let n = 1; n <= count; n++) {
        const page = await pdf.getPage(n);
        const unscaled = page.getViewport({ scale: 1 });
        const width = Math.max(container.clientWidth, 280);
        const viewport = page.getViewport({ scale: width / unscaled.width });
        const canvas = document.createElement("canvas");
        canvas.width = Math.floor(viewport.width * ratio);
        canvas.height = Math.floor(viewport.height * ratio);
        canvas.style.width = "100%";
        canvas.style.height = "auto";
        canvas.setAttribute("role", "img");
        canvas.setAttribute("aria-label", (pageLabel || "Page {0} / {1}").replace("{0}", n).replace("{1}", pdf.numPages));
        canvas.className = "bz-pdf-page";
        container.appendChild(canvas);
        const context = canvas.getContext("2d");
        await page.render({ canvasContext: context, viewport: viewport, transform: ratio !== 1 ? [ratio, 0, 0, ratio, 0, 0] : null }).promise;
    }
    return pdf.numPages;
}

export async function dispose(container) {
    const task = tasks.get(container);
    if (task) { try { await task.destroy(); } catch { /* ignoré */ } tasks.delete(container); }
}
