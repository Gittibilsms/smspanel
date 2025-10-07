document.addEventListener('DOMContentLoaded', function () {
    const modalEl = document.getElementById('addCompanyModal');

    if (!modalEl) return; // Stop if modal isn't present

    modalEl.addEventListener('shown.bs.modal', function () {
        const el = document.querySelector('#stepper2');

        if (el && !window.stepper2) {
            window.stepper2 = new Stepper(el, { linear: false, animation: true });
        }
    });

    modalEl.addEventListener('hidden.bs.modal', function () {
        if (window.stepper2) {
            window.stepper2.reset();
        }

        const form = modalEl.querySelector('#addCompanyForm');
        if (form) form.reset();
    });
});
