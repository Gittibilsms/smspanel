
    function showSmsFormModal() {
        var myModal = new bootstrap.Modal(document.getElementById('smsModal'));
        myModal.show();
    }

    document.getElementById('smsModal').addEventListener('submit', function (e) {
        e.preventDefault();

        const formData = new FormData(this);

        fetch('/Home/SendSms', {
            method: 'POST',
            body: formData
        })
            .then(res => res.ok ? alert('SMS sent!') : alert('Failed'))
            .catch(err => alert('Error'));
    });




    $('#selectFirm').select2({
        theme: "bootstrap-5",
        width: $(this).data('width') ? $(this).data('width') : $(this).hasClass('w-100') ? '100%' : 'style',
        placeholder: $(this).data('placeholder'),
        allowClear: true
    });
$('#selectDirectory').select2({
    theme: "bootstrap-5",
    width: $(this).data('width') ? $(this).data('width') : $(this).hasClass('w-100') ? '100%' : 'style',
    placeholder: $(this).data('placeholder'),
    allowClear: true
});
$('#numberUpload').FancyFileUpload({
    params: {
        action: 'fileuploader'
    },
    maxfilesize: 1000000
});

flatpickr(".date-time", {
    enableTime: true,
    dateFormat: "Y-m-d H:i",
});

$(document).ready(function () {
    const $dependentInputs = $('.dependent-fields').find('input, select, textarea, button');

    // Disable all fields inside .dependent-fields on load
    $dependentInputs.prop('disabled', true).addClass('disabled');

    // Watch for selectFirm changes
    $('#selectFirm').on('change', function () {
        const isValid = $(this).val()?.trim() !== "";

        // Enable/disable the rest
        $dependentInputs.prop('disabled', !isValid);

        // Manually disable file upload plugin if needed
        if (!isValid) {
            $('#numberUpload').closest('.fancy-file-upload').addClass('disabled');
        } else {
            $('#numberUpload').closest('.fancy-file-upload').removeClass('disabled');
        }
    });
});

$('#smsModal').on('hidden.bs.modal', function () {
    // Reset the form
    $('#smsForm')[0].reset();

    // Reset Select2 if used
    $('#smsForm select').each(function () {
        if ($(this).hasClass('select2-hidden-accessible')) {
            $(this).val(null).trigger('change');
        }
    });

    // Clear Fancy File Upload
    $('#numberUpload').FancyFileUpload('reset');

    // Optionally re-disable dependent fields
    toggleDependentFields(false);
});

$(document).ready(function () {
    $(document).ready(function () {
        var table = $('#companiesList').DataTable({
            lengthChange: false,
            buttons: ['copy', 'excel', 'pdf', 'print']
        });

        table.buttons().container()
            .appendTo('#companiesList_wrapper .col-md-6:eq(0)');
    });
});

$(document).ready(function () {
    $("#show_hide_password a").on('click', function (event) {
        event.preventDefault();
        var passwordInput = $('#show_hide_password input');
        var icon = $('#show_hide_password i');

        if (passwordInput.attr("type") === "text") {
            passwordInput.attr('type', 'password');
            icon.removeClass("bi-eye-fill").addClass("bi-eye-slash-fill");
        } else if (passwordInput.attr("type") === "password") {
            passwordInput.attr('type', 'text');
            icon.removeClass("bi-eye-slash-fill").addClass("bi-eye-fill");
        }
    });
});