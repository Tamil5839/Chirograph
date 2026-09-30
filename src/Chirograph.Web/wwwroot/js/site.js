// Copy buttons: <button data-copy="text">. Kept out of the markup so the Content-Security-Policy can forbid inline script.
document.addEventListener("click", async (event) => {
    const button = event.target.closest("[data-copy]");
    if (!button || !navigator.clipboard) return;
    await navigator.clipboard.writeText(button.dataset.copy);
    const original = button.textContent;
    button.textContent = "Copied";
    setTimeout(() => { button.textContent = original; }, 1500);
});
