document.addEventListener("click", (event) => {
    if (!(event.target instanceof Element)) {
        return;
    }

    const openButton = event.target.closest("[data-logout-dialog-open]");
    if (openButton instanceof HTMLButtonElement) {
        const dialog = document.getElementById(openButton.dataset.logoutDialogOpen);
        if (dialog instanceof HTMLDialogElement) {
            dialog.showModal();
        }

        return;
    }

    const closeButton = event.target.closest("[data-logout-dialog-close]");
    if (closeButton instanceof HTMLButtonElement) {
        const dialog = closeButton.closest("dialog");
        if (dialog instanceof HTMLDialogElement) {
            dialog.close();
        }
    }
});
