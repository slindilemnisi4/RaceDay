const roleSelect = document.getElementById("registration-role");
const organiserCodeField = document.getElementById("organiser-code-field");
const organiserCodeInput = document.getElementById("OrganiserRegistrationCode");

function updateOrganiserCodeVisibility() {
    const organiserSelected = roleSelect?.value === "0";
    if (organiserCodeField) {
        organiserCodeField.hidden = !organiserSelected;
    }
    if (organiserCodeInput) {
        organiserCodeInput.required = organiserSelected === true;
    }
}

roleSelect?.addEventListener("change", updateOrganiserCodeVisibility);
updateOrganiserCodeVisibility();
