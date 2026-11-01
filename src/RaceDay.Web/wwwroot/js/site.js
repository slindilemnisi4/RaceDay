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

    const galleryTrigger = event.target.closest("[data-event-gallery-open]");
    if (galleryTrigger instanceof HTMLButtonElement) {
        openEventGallery(galleryTrigger);
    }

    const galleryClose = event.target.closest("[data-gallery-close]");
    if (galleryClose instanceof HTMLButtonElement) {
        galleryClose.closest("dialog")?.close();
    }

    const previous = event.target.closest("[data-gallery-previous]");
    if (previous instanceof HTMLButtonElement) {
        moveGallery(previous.closest("dialog"), -1);
    }

    const next = event.target.closest("[data-gallery-next]");
    if (next instanceof HTMLButtonElement) {
        moveGallery(next.closest("dialog"), 1);
    }
});

document.addEventListener("keydown", (event) => {
    const dialog = document.querySelector(".event-media-dialog[open]");
    if (!(dialog instanceof HTMLDialogElement)) {
        return;
    }

    if (event.key === "ArrowLeft") {
        event.preventDefault();
        moveGallery(dialog, -1);
    } else if (event.key === "ArrowRight") {
        event.preventDefault();
        moveGallery(dialog, 1);
    }
});

document.addEventListener("click", (event) => {
    if (event.target instanceof HTMLDialogElement
        && event.target.classList.contains("event-media-dialog")) {
        event.target.close();
    }
});

function openEventGallery(trigger) {
    const dialog = document.getElementById(trigger.dataset.eventGalleryOpen);
    if (!(dialog instanceof HTMLDialogElement)) {
        return;
    }

    const triggers = galleryTriggers(dialog.id);
    const selectedIndex = Number.parseInt(trigger.dataset.galleryIndex ?? "0", 10);
    if (triggers.length === 0 || !Number.isInteger(selectedIndex)) {
        return;
    }

    dialog.dataset.activeIndex = String(selectedIndex);
    dialog.__returnFocusTo = trigger;
    updateGallery(dialog);
    dialog.showModal();
    document.body.classList.add("event-media-dialog-open");
    dialog.querySelector("[data-gallery-close]")?.focus();
}

function galleryTriggers(galleryId) {
    return [...document.querySelectorAll("[data-event-gallery-open]")]
        .filter((trigger) => trigger.dataset.eventGalleryOpen === galleryId);
}

function moveGallery(dialog, amount) {
    if (!(dialog instanceof HTMLDialogElement)) {
        return;
    }

    const triggers = galleryTriggers(dialog.id);
    const currentIndex = Number.parseInt(dialog.dataset.activeIndex ?? "0", 10);
    const nextIndex = currentIndex + amount;
    if (nextIndex < 0 || nextIndex >= triggers.length) {
        return;
    }

    dialog.dataset.activeIndex = String(nextIndex);
    updateGallery(dialog);
}

function updateGallery(dialog) {
    const triggers = galleryTriggers(dialog.id);
    const index = Number.parseInt(dialog.dataset.activeIndex ?? "0", 10);
    const active = triggers[index];
    if (!(active instanceof HTMLButtonElement)) {
        return;
    }

    const image = dialog.querySelector("[data-gallery-image]");
    const video = dialog.querySelector("[data-gallery-video]");
    const fallback = dialog.querySelector("[data-gallery-fallback]");
    const previous = dialog.querySelector("[data-gallery-previous]");
    const next = dialog.querySelector("[data-gallery-next]");
    const counter = dialog.querySelector("[data-gallery-counter]");

    if (video instanceof HTMLVideoElement) {
        video.pause();
        video.removeAttribute("src");
        video.load();
        video.onended = null;
        video.onerror = null;
    }

    if (fallback instanceof HTMLElement) {
        fallback.hidden = true;
    }

    if (active.dataset.galleryType === "video" && video instanceof HTMLVideoElement) {
        if (image instanceof HTMLImageElement) {
            image.hidden = true;
            image.removeAttribute("src");
        }

        video.src = active.dataset.gallerySrc ?? "";
        video.muted = true;
        video.defaultMuted = true;
        video.playsInline = true;
        video.loop = true;
        video.hidden = false;
        video.onerror = () => {
            video.hidden = true;
            if (fallback instanceof HTMLElement) {
                fallback.hidden = false;
            }
        };
        video.play().catch(() => {
            video.hidden = true;
            if (fallback instanceof HTMLElement) {
                fallback.hidden = false;
            }
        });
    } else if (image instanceof HTMLImageElement) {
        if (video instanceof HTMLVideoElement) {
            video.hidden = true;
        }

        image.src = active.dataset.gallerySrc ?? "";
        image.alt = active.getAttribute("aria-label") ?? "Event image";
        image.hidden = false;
    }

    if (previous instanceof HTMLButtonElement) {
        previous.disabled = index <= 0;
    }

    if (next instanceof HTMLButtonElement) {
        next.disabled = index >= triggers.length - 1;
    }

    if (counter instanceof HTMLElement) {
        counter.textContent = `${index + 1} / ${triggers.length}`;
    }
}

function closeEventGallery(dialog) {
    document.body.classList.remove("event-media-dialog-open");
    const video = dialog.querySelector("[data-gallery-video]");
    if (video instanceof HTMLVideoElement) {
        video.pause();
        video.removeAttribute("src");
        video.load();
    }

    const returnFocusTo = dialog.__returnFocusTo;
    if (returnFocusTo instanceof HTMLElement) {
        returnFocusTo.focus();
    }
}

document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll(".event-media-dialog").forEach((dialog) => {
        dialog.addEventListener("close", () => closeEventGallery(dialog));
        const stage = dialog.querySelector("[data-gallery-stage]");
        if (stage instanceof HTMLElement) {
            stage.addEventListener("pointerdown", (event) => {
                dialog.dataset.pointerStartX = String(event.clientX);
            });
            stage.addEventListener("pointerup", (event) => {
                const startX = Number.parseFloat(dialog.dataset.pointerStartX ?? "");
                delete dialog.dataset.pointerStartX;
                const distance = event.clientX - startX;
                if (Number.isFinite(distance) && Math.abs(distance) > 50) {
                    moveGallery(dialog, distance < 0 ? 1 : -1);
                }
            });

            if ("IntersectionObserver" in window) {
                const observer = new IntersectionObserver((entries) => {
                    for (const entry of entries) {
                        const video = entry.target;
                        if (video instanceof HTMLVideoElement && !entry.isIntersecting) {
                            video.pause();
                        } else if (video instanceof HTMLVideoElement
                            && entry.isIntersecting
                            && !video.paused
                            && video.muted) {
                            video.play().catch(() => {});
                        }
                    }
                }, { root: dialog, threshold: 0.1 });

                const video = dialog.querySelector("[data-gallery-video]");
                if (video instanceof HTMLVideoElement) {
                    observer.observe(video);
                }
            }
        }
    });

    document.querySelectorAll("[data-event-image-input]").forEach((input) => {
        initializeImageUpload(input);
    });
    document.querySelectorAll("[data-event-video-input]").forEach((input) => {
        initializeVideoUpload(input);
    });
});

function initializeImageUpload(input) {
    if (!(input instanceof HTMLInputElement)) {
        return;
    }

    const preview = document.querySelector("[data-event-image-preview]");
    const count = document.querySelector("[data-event-image-count]");
    const retainedImageCount = () => document.querySelectorAll(
        'input[name="RetainedMediaIds"]:checked').length;
    const objectUrls = new Set();
    const render = () => {
        if (!(preview instanceof HTMLElement) || !(count instanceof HTMLElement)) {
            return;
        }

        const files = [...(input.files ?? [])];
        objectUrls.forEach((url) => URL.revokeObjectURL(url));
        objectUrls.clear();
        count.textContent = `${retainedImageCount() + files.length} of 5 images selected or retained.`;
        preview.replaceChildren();
        files.forEach((file, index) => {
            const item = document.createElement("div");
            item.className = "event-media-upload__preview-item";
            const image = document.createElement("img");
            image.alt = `Preview of ${file.name}`;
            image.src = URL.createObjectURL(file);
            objectUrls.add(image.src);
            const remove = document.createElement("button");
            remove.type = "button";
            remove.className = "event-retry";
            remove.textContent = "Remove";
            remove.setAttribute("aria-label", `Remove ${file.name}`);
            remove.addEventListener("click", () => {
                const transfer = new DataTransfer();
                [...(input.files ?? [])].forEach((candidate, candidateIndex) => {
                    if (candidateIndex !== index) {
                        transfer.items.add(candidate);
                    }
                });
                input.files = transfer.files;
                render();
            });
            item.append(image, remove);
            preview.append(item);
        });
    };

    input.addEventListener("change", () => {
        const limit = Math.max(0, 5 - retainedImageCount());
        if ((input.files?.length ?? 0) > limit) {
            const transfer = new DataTransfer();
            [...(input.files ?? [])].slice(0, limit).forEach((file) => transfer.items.add(file));
            input.files = transfer.files;
            count.textContent = `Only ${limit} additional image(s) fit this event.`;
        }

        render();
    });
    document.querySelectorAll('input[name="RetainedMediaIds"]').forEach((checkbox) => {
        checkbox.addEventListener("change", render);
    });
    render();
}

function initializeVideoUpload(input) {
    if (!(input instanceof HTMLInputElement)) {
        return;
    }

    const preview = document.querySelector("[data-event-video-preview]");
    const retainedVideo = document.querySelector("[data-retain-event-video]");
    input.addEventListener("change", () => {
        if (!(preview instanceof HTMLVideoElement)) {
            return;
        }

        preview.pause();
        preview.removeAttribute("src");
        preview.hidden = true;
        const file = input.files?.[0];
        if (!file) {
            return;
        }

        if (retainedVideo instanceof HTMLInputElement && retainedVideo.checked) {
            retainedVideo.checked = false;
        }

        preview.src = URL.createObjectURL(file);
        preview.muted = true;
        preview.defaultMuted = true;
        preview.playsInline = true;
        preview.loop = true;
        preview.hidden = false;
        preview.play().catch(() => {});
    });
}
