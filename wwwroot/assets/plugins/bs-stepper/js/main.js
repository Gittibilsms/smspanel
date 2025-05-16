var stepper2;

document.getElementById('addCompanyModal').addEventListener('shown.bs.modal', function () {
    const el = document.querySelector('#stepper2');
    if (el && !stepper2) {
        stepper2 = new Stepper(el, { linear: false, animation: true });
    }
});

document.getElementById('addCompanyModal').addEventListener('hidden.bs.modal', function () {
    if (stepper2) {
        stepper2.reset();
    }
});