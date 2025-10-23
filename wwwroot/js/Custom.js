
function updateLogoByTheme() {
    const theme = document.documentElement.getAttribute('data-bs-theme');
    const lightLogo = document.querySelector('.logo-light');
    const darkLogo = document.querySelector('.logo-dark');

    if (theme === 'light') {
        lightLogo.style.display = 'none';
        darkLogo.style.display = 'block';
    }
    else if (theme === 'bodered-theme') {
        lightLogo.style.display = 'none';
        darkLogo.style.display = 'block';
    }
    else if (theme === 'semi-dark') {
        lightLogo.style.display = 'block';
        darkLogo.style.display = 'none';
    }
    else {
        lightLogo.style.display = 'block';
        darkLogo.style.display = 'none';
    }

}
let reloadScheduled = false;
function checkForNewSession() {
    if (reloadScheduled) return;
    const current = sessionStorage.getItem('sessionVersion') || '';
    $.post('/Account/RefreshSession', res => {
        if (res.success && res.version !== current && !reloadScheduled) {
            reloadScheduled = true;
            sessionStorage.setItem('sessionVersion', res.version);
            location.reload(true);
        }
    });
}

// on load
document.addEventListener('DOMContentLoaded', () => {
    updateLogoByTheme();
    checkForNewSession();
});
window.addEventListener('focus', checkForNewSession);
// Watch for theme changes
const observer = new MutationObserver(updateLogoByTheme);
observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-bs-theme'] });
window.showToastrFromTempData = function (successMessage, errorMessage) {
    if (successMessage) {
        toastr.success(successMessage);
    } else if (errorMessage) {
        toastr.error(errorMessage);
    }
};
const canEditUsers = $('#permission-flags-users').data('can-edit-users') === true || $('#permission-flags-users').data('can-edit-users') === "true";
const canEditFirm = $('#permission-flags-firm').data('can-edit-firm') === true || $('#permission-flags-firm').data('can-edit-firm') === "true";
const isCompanyUser = $('#permission-flags').data('is-company-user') === true || $('#permission-flags').data('is-company-user') === "true";
$("#loginForm").submit(function (e) {
    e.preventDefault(); // ✅ important
    $('#globalSpinnerOverlay').show();
    $.post("/Account/Login", $(this).serialize())
        .done(function (res) {
            $('#globalSpinnerOverlay').hide();
            if (res.require2FA) {
                window.location.href = "/Account/Verify2FA";
            } else if (res.requireEmail2FA) {
                window.location.href = "/Account/VerifyEmail2FA";
            }
            else {
                window.location.href = "/Home";
            }
        })
        .fail(function (xhr) {
            $('#globalSpinnerOverlay').hide();
            var errorText = xhr.responseJSON.value || "Bir hata oluştu.";
            $("#loginError").html('<div class="alert alert-danger text-center">' + errorText + '</div>');
        });
});
// Show SMS Modal
function debounce(func, delay = 300) {
    let timeout;
    return function (...args) {
        clearTimeout(timeout);
        timeout = setTimeout(() => func.apply(this, args), delay);
    };
}
const SEGMENT_UNITS = 155;
const MAX_UNITS = 755;
function calculateMessageLength(text) {
    let count = 0;
    for (let ch of text) {
        if (ch === '\n') {
            // newline = 2 units
            count += 2;
        }
        else if (/^[A-Za-z0-9 ]$/.test(ch)) {
            count += 1;
        }
        else {
            count += 2;
        }
    }
    return count;
}
let phoneNumbersCache = {
    lastText: '',
    rawNumbers: [],
    validCount: 0,
    invalidCount: 0,
    totalCount: 0
};
function showSmsFormModal() {
    $.get('/Home/LoadSendSmsModal', function (html) {
        $('#smsModalContent').html(html);
        $('#smsModal').modal('show');

        setTimeout(function () {
            initFancyUploader();
            initFlatpickr();
            $('#smsForm').on('submit', function (e) {
                e.preventDefault();
                sendSmsAjax();
            });

            // --- Disable dependent fields initially ---
            const $dependentInputs = $('.dependent-fields').find('input, select, textarea, button');
            $dependentInputs.prop('disabled', true).addClass('disabled');


            $('#smsModal')
                .off('hidden.bs.modal.smsModal')
                .on('hidden.bs.modal.smsModal', () => {
                    $('#selectFirm')
                        .off('change.smsModal')      // unbind your handler
                        .select2('destroy');         // destroy the widget
                });
            // Reinitialize select2
            // Destroy any existing Select2 instance first
            if ($('#selectFirm').hasClass('select2-hidden-accessible')) {
                $('#selectFirm').select2('destroy');
            }

            // Small delay to ensure DOM is ready
            setTimeout(function () {
                // Reinitialize select2
                $('#selectFirm').select2({
                    theme: "bootstrap-5",
                    width: '100%',
                    placeholder: function () {
                        return $(this).data('placeholder');
                    },
                    allowClear: true,
                    dropdownParent: $('#smsModal') // Important for modals
                });



                // Rebind change event
                $('#selectFirm').on('change.smsModal', function () {
                    if (!$('#smsModal').is(':visible')) {
                        return; // modal is closed → skip all AJAX
                    }
                    const selectedOption = this.options[this.selectedIndex];
                    const credit = selectedOption ? selectedOption.getAttribute('data-credit') || 0 : 0;

                    document.getElementById('AvailableCredit').value = credit;
                    document.getElementById('availableCredit').innerText = parseFloat(credit).toLocaleString('en-US', {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2
                    });

                    const selected = parseInt($(this).val());
                    const isValid = !isNaN(selected) && selected > 0;

                    $dependentInputs.prop('disabled', !isValid).toggleClass('disabled', !isValid);
                    $('#numberUpload').closest('.fancy-file-upload').toggleClass('disabled', !isValid);
                    const companyId = $(this).val();
                    $('#PhoneNumbers').val('');
                    $.get(`/Home/GetCompanyPrices?companyId=${companyId}`, function (data) {
                        $('#companyLowPrice').val(data.low);
                        $('#companyMediumPrice').val(data.medium);
                        $('#companyHighPrice').val(data.high);
                        updateSmsPricePerUnit();
                        const apiId = data.selectedApiId ?? '';
                        $('.formChooseApi').val(String(apiId)).trigger('change');
                    });
                    $.get(`/Home/GetByCompany?companyId=${companyId}`, function (res) {
                        if (res.success && Array.isArray(res.data.$values)) {
                            const options = res.data.$values.map(d =>
                                `<option value="${d.directoryId}">${d.directoryName}</option>`
                            );
                            $('#selectDirectory').html(`<option value="">${window.localizedTextDT.selectdirectory}</option>` + options.join(''));
                        } else {
                            toastr.error('Failed to load directories.');
                        }
                    });
                });
                if ($('#selectFirm').is(':disabled')) {
                    $('#selectFirm').trigger('change');
                }
            }, 200);
            // --- Message input handler ---


            $('#selectDirectory').on('change', function () {
                const directoryId = $(this).val();

                if (!directoryId) {
                    $('#PhoneNumbers').val('');
                    return;
                }

                $.get(`/Home/GetDirectoryNumbers?directoryId=${directoryId}`, function (res) {
                    if (res.success && res.numbers && res.numbers.$values) {
                        const combined = res.numbers.$values.join('\n');
                        $('#PhoneNumbers').val(combined);
                        $('#PhoneNumbers').trigger('input'); // Optional for count update
                    } else {
                        toastr.error(res.message || 'Failed to load numbers.');
                    }
                });
            });
            // 1️⃣ Constants

            // 2️⃣ beforeinput: block any insertion that would go past MAX_UNITS
            const msgEl = document.getElementById('message');
            msgEl.addEventListener('beforeinput', function (e) {
                // only worry about insertions, not deletion/navigation
                if (!e.data && e.inputType !== 'insertLineBreak' && e.inputType !== 'insertFromPaste') {
                    return;
                }

                const start = this.selectionStart;
                const end = this.selectionEnd;
                const before = this.value.slice(0, start);
                const after = this.value.slice(end);
                const inserted = e.inputType === 'insertLineBreak' ? '\n'
                    : (e.inputType === 'insertFromPaste' ? (e.data || '')
                        : e.data || '');

                // build the would‑be new text
                const newText = before + inserted + after;
                const newLen = calculateMessageLength(newText);

                if (newLen > MAX_UNITS) {
                    // block it entirely
                    e.preventDefault();
                }
            });

            // 3️⃣ input: update the counter & price (fallback trim if needed)
            $('#message').on('input', debounce(function () {
                const text = this.value;
                const length = calculateMessageLength(text);

                // If somehow over the max, just trim the value
                if (length > MAX_UNITS) {
                    // (You could repeat your trimming logic here if desired)
                    this.value = text.slice(0, this.value.length - 1);
                }

                const smsCount = length === 0
                    ? 0
                    : Math.ceil(length / SEGMENT_UNITS);

                $('#charCount')
                    .text(`${length} / ${SEGMENT_UNITS} (${smsCount} SMS)`)
                    .toggleClass('text-danger', length > SEGMENT_UNITS);
                $('#TotalSmsCount').val(smsCount);

                updateSmsPricePerUnit();
            }, 300));

            // 4️⃣ your length calculator


            function updateOrderCost() {
                const smsCount = parseInt($('#smsCount').text().replace(/\D/g, '')) || 0;
                const smsPrice = parseFloat($('#SmsPrice').val()) || 0;

                const message = $('#message').val() || "";
                const smsParts = calculateSmsParts(message);

                const orderCost = smsCount * smsParts * smsPrice;

                // 🚩 Always parse credit correctly, fallback to 0
                const credit = parseFloat($('#AvailableCredit').val()) || 0;

                // Update UI
                $('#orderCost').text(orderCost.toFixed(2));



                // Correct logic
                const statusElement = document.getElementById('creditStatus');
                if (orderCost <= credit) {
                    statusElement.innerText = "✅ Enough credit — you can send this order!";
                    statusElement.style.color = "green";
                } else {
                    statusElement.innerText = "⚠️ Not enough credit! Please add more credit.";
                    statusElement.style.color = "red";
                }
            }

            function isGsmCharacter(code) {
                const basicGsm =
                    "@£$¥èéùìòÇØøÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ " +
                    "!" + '"#¤%&\'()*+,-./0123456789:;<=>?' +
                    "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ`¿" +
                    "abcdefghijklmnopqrstuvwxyzäöñüà";
                return basicGsm.includes(String.fromCharCode(code));
            }

            function isTurkishChar(ch) {
                return 'ŞşİıÇçÖöÜüĞğ'.includes(ch);
            }

            $('#PhoneNumbers').on('input', function () {
                updateSmsPriceAndCost();
            });

            function calculateSmsParts(message) {
                const length = message.length;

                if (length === 0) {
                    return 0;
                } else if (length <= 160) {
                    return 1;
                } else {
                    return Math.ceil((length - 160) / 153) + 1;
                }
            }


            // Handle file deletion - Fixed event handler
            $(document).on('click', '.ff_fileupload_remove_file', function (e) {
                e.preventDefault();
                e.stopPropagation();

                const $row = $(this).closest('[data-file-id]');
                const fileId = $row.attr('data-file-id');

                // 1) clear the Excel preview
                $('#filePreview').empty();

                // 2) clear out *just* the name/number buttons
                $('#nameButtons, #numberButtons').empty();

                // 3) hide the entire selector rows again
                $('#nameSelectors, #numberSelectors').hide();

                // 4) remove the placeholder from the message textarea
                const label = $('#SelectedCustomColumn').val();
                const placeholder = `{${label}}`;
                let text = $('#message').val() || '';
                text = text.split(placeholder).join('').trim();
                $('#message').val(text);

                // 5) clear your hidden fields so they’re fresh next time
                $('#SelectedCustomColumn, #SelectedCustomColumnKey').val('');
                $('#SelectedNumberColumn,   #SelectedNumberColumnKey').val('');

                // 6) clean up your uploadedNumbersMap as before
                if (fileId && uploadedNumbersMap.has(fileId)) {
                    uploadedNumbersMap.delete(fileId);
                    updateTextareaFromMap();
                } else {
                    console.warn('⚠ Could not find fileId in uploadedNumbersMap for deletion.');
                }
            });

            $('#previewSmsBtn').on('click', function () {
                const message = $('#message').val().trim();
                //const apiText = $('#chooseApi option:selected').text();
                const numbers = $('#PhoneNumbers').val().trim();

                if (!message || !numbers) {
                    toastr.error(window.localizedTextDT.Pleasefillapi || "Please fill all the required fields.");

                    return;
                }

                // Format preview to show only first 10 numbers + "and X more..."
                const numberList = numbers
                    .split(/[,;\n\r]/)
                    .map(n => n.trim())
                    .filter(n => n !== "");

                const visibleNumbers = numberList.slice(0, 10).join(", ");
                const moreCount = numberList.length > 10 ? ` ...and ${numberList.length - 10} more` : "";

                $('#previewMessage').text(message);
                $('#previewRecipients').val(visibleNumbers + moreCount);
                // $('#previewApi').text(apiText);

                const previewModal = new bootstrap.Modal(document.getElementById('smsPreviewModal'));
                previewModal.show();
            });
        }, 100);
    });
}
function updateSmsPriceAndCost() {
    const count = updateNumbersCount(); // ✅ now reused
    const unitPrice = getUnitPriceBasedOnCount(count);

    $('#smsPrice').text(unitPrice);

    // If you want to show total cost:
    const totalCost = (count * parseFloat(unitPrice || 0)).toFixed(3);
    $('#totalCost').text(totalCost); // optional if you show it
}

function updateNumbersCount() {
    const text = document.getElementById('PhoneNumbers').value;

    // ✅ Use cache to avoid re-processing same data
    if (phoneNumbersCache.lastText === text) {
        updateDisplayCounts();
        return phoneNumbersCache.totalCount;
    }

    // ✅ Only process if text changed
    phoneNumbersCache.lastText = text;

    // ✅ Optimized splitting - use single regex and filter in one pass
    const rawNumbers = text.split(/[,;\n\r]+/).filter(n => n.trim() !== "");
    phoneNumbersCache.rawNumbers = rawNumbers;
    phoneNumbersCache.totalCount = rawNumbers.length;

    // ✅ For large datasets, use batch processing to avoid blocking UI
    if (rawNumbers.length > 1000) {
        processNumbersBatched(rawNumbers);
    } else {
        processNumbersSync(rawNumbers);
    }

    updateDisplayCounts();
    return phoneNumbersCache.totalCount;
}
function processNumbersSync(rawNumbers) {
    let validCount = 0;
    let invalidCount = 0;

    for (const n of rawNumbers) {
        const normalized = normalizePhoneNumber(n.trim());
        if (normalized) {
            validCount++;
        } else {
            invalidCount++;
        }
    }

    phoneNumbersCache.validCount = validCount;
    phoneNumbersCache.invalidCount = invalidCount;
}
function processNumbersBatched(rawNumbers) {
    let validCount = 0;
    let invalidCount = 0;
    let currentIndex = 0;
    const batchSize = 500; // Process 500 numbers at a time

    function processBatch() {
        const endIndex = Math.min(currentIndex + batchSize, rawNumbers.length);

        for (let i = currentIndex; i < endIndex; i++) {
            const normalized = normalizePhoneNumber(rawNumbers[i].trim());
            if (normalized) {
                validCount++;
            } else {
                invalidCount++;
            }
        }

        currentIndex = endIndex;

        // Update progress for large datasets
        if (rawNumbers.length > 5000) {
            const progress = Math.round((currentIndex / rawNumbers.length) * 100);
            document.getElementById('validCount').innerText = `✅ İşleme... ${progress}%`;
        }

        if (currentIndex < rawNumbers.length) {
            // Continue processing in next frame
            requestAnimationFrame(processBatch);
        } else {
            // Processing complete
            phoneNumbersCache.validCount = validCount;
            phoneNumbersCache.invalidCount = invalidCount;
            updateDisplayCounts();
        }
    }

    // Start batch processing
    requestAnimationFrame(processBatch);
}

function updateDisplayCounts() {
    const { totalCount, validCount, invalidCount } = phoneNumbersCache;
    document.getElementById('smsCount').innerText = window.localizedTextDT.TotalNumbers.replace("{0}", totalCount);
    document.getElementById('validCount').innerText = `✅ ` + window.localizedTextDT.validcounts.replace("{0}", validCount);
    document.getElementById('invalidCount').innerText = `❌ ` + window.localizedTextDT.invalidcounts.replace("{0}", invalidCount);
}
function normalizePhoneNumber(rawNumber) {
    if (!rawNumber) return null;

    // Normalize Unicode, remove spaces, dashes, parens, plus
    let number = rawNumber.normalize('NFKC').replace(/[\s\u00A0\-\(\)\+]/g, '');

    // Remove any non-digit characters
    number = number.replace(/[^\d]/g, '');

    // Extract last 10 digits starting with 5
    const match = number.match(/5\d{9}$/);

    if (!match) {
        //console.log(`❌ Invalid: "${rawNumber}" → "${number}" (length=${number.length})`);
        return null; // Will fallback to raw in updateTextareaFromMap
    }

    const cleanedNumber = match[0];



    return '90' + cleanedNumber;
}
function setLanguage(culture) {
    // Set cookie for 1 year
    document.cookie = ".AspNetCore.Culture=c=" + culture + "|uic=" + culture + "; path=/; max-age=" + (60 * 60 * 24 * 365);

    // Reload page (you can also redirect to Home or stay on current page)
    location.reload();
}
function sendSmsAjax() {
    const smsForm = document.getElementById('smsForm');
    const formData = new FormData(smsForm);
    const submitButton = document.getElementById('sendSmsButton');
    const messageText = document.getElementById('message').value;
    const totalUnits = calculateMessageLength(messageText);
    const TotalSmsCount = totalUnits === 0
        ? 0
        : Math.ceil(totalUnits / SEGMENT_UNITS);

    // 2️⃣ Append it to the form data
    formData.append('TotalSmsCount', TotalSmsCount);
    formData.set('FileMode', $('input[name="fileMode"]:checked').val());
    submitButton.disabled = true;
    $('#globalSpinnerOverlay').show();

    fetch('/Home/SendSms', {
        method: 'POST',
        body: formData
    })
        .then(async res => {
            const result = await res.json().catch(() => ({}));

            if (res.ok) {
                // SUCCESS
                const message = typeof result.message === 'object' ? result.message.value : result.message;
                alert('✅ ' + (message || 'SMS başarıyla gönderildi.'));
            } else {
                // FAILURE (handled here, not in .catch)
                const errorText = result?.value?.value || result?.value || result?.message || 'SMS gönderimi başarısız oldu.';
                alert('❌ ' + errorText);
            }

            // Close modals
            const previewModalEl = document.getElementById('smsPreviewModal');
            bootstrap.Modal.getInstance(previewModalEl)?.hide();

            const smsModalEl = document.getElementById('smsModal');
            bootstrap.Modal.getInstance(smsModalEl)?.hide();

            // Reload table & reset form
            $('#ordersList').DataTable().ajax.reload(null, false);
            smsForm.reset();
            $('#numberUpload').FancyFileUpload('reset');
            $('#PhoneNumbers').val('');
        })
        .catch(err => {
            // Only for network errors or unhandled JS issues
            alert('⚠ Ağ hatası oluştu. Lütfen tekrar deneyin.');
            console.error('Unexpected fetch error:', err);

            // Close modals just in case
            bootstrap.Modal.getInstance(document.getElementById('smsPreviewModal'))?.hide();
            bootstrap.Modal.getInstance(document.getElementById('smsModal'))?.hide();
        })
        .finally(() => {
            submitButton.disabled = false;
            $('#globalSpinnerOverlay').hide();
        });
}

// Handle SMS Form Submission


function updateTextareaFromMap() {
    if (suppressMapUpdate) return;

    // 1) collect every raw string in entry.numbers
    let allRaw = [];
    uploadedNumbersMap.forEach(entry => {
        if (Array.isArray(entry.numbers)) {
            allRaw = allRaw.concat(entry.numbers);
        }
    });

    // 2) normalize & classify
    const validNumbers = [];
    const invalidNumbers = [];
    allRaw.forEach(raw => {
        const trimmed = raw.trim();
        const num = normalizePhoneNumber(trimmed);
        if (num) {
            validNumbers.push(num);
        }
        else {
            // only keep “invalid” entries if they still look like a number
            // (e.g. “123-4567” or “+1 (800) 555-1212”)
            if (/^[\d\+\-\s\(\)]+$/.test(trimmed)) {
                invalidNumbers.push(trimmed);
            }
        }
    });

    // 3) uniq them
    const uniqueValid = [...new Set(validNumbers)];
    const uniqueInvalid = [...new Set(invalidNumbers)];

    // 4) write _only_ numeric lines (valid + numeric-invalid) into the textarea
    $('#PhoneNumbers').val(
        uniqueValid
            .concat(uniqueInvalid)
            .join('\n')
    );

    // 5) update counts
    $('#validCount').text(`✅ Valid: ${uniqueValid.length}`);
    $('#invalidCount').text(`❌ Numeric-but-format-invalid: ${uniqueInvalid.length}`);

    updateNumbersCount();

    // 6) re-enable textarea if empty
    if (!uniqueValid.length && !uniqueInvalid.length) {
        $('#PhoneNumbers, #numberUpload').prop('disabled', false);
    }
}


$(document).on('click', '#confirmSendSms', function () {
    sendSmsAjax();
});
//smsForm.addEventListener('submit', function (e) {
//    e.preventDefault();
//    sendSmsAjax();
//});


$(function () {
    $(document).on('change', 'input[name="fileMode"]', function () {
        const mode = $('input[name="fileMode"]:checked').val();
        const isCustom = mode === 'custom';
        const prefix = $('input[name="fileMode"]:checked').data('column-prefix');

        // update accepted extensions
        $('#numberUpload').attr(
            'accept',
            isCustom ? '.xls,.xlsx' : '.csv,.txt,.xls,.xlsx'
        );

        // refresh the prefix on the radio buttons (if you need it)
        $('#fileModeStandard, #fileModeCustom').data('column-prefix', prefix);

        if (isCustom) {
            // only build/show selectors if a file really is loaded
            if (lastFile) {
                $('#PhoneNumbers').val('');
                updateDisplayCounts();
                buildCustomSelectors(lastFile, lastFileData);
            }
            // otherwise, keep them hidden until someone uploads
            else {
                $('#nameSelectors, #numberSelectors').hide();
            }
        } else {
            // standard mode → always hide the custom pickers
            $('#nameSelectors, #numberSelectors').hide();
        }
    });

    // initialize on page load
    $('input[name="fileMode"]:checked').trigger('change');
    initFancyUploader();
});


let lastFileData = null;      // store the FancyFileUpload data obj
let lastFile = null;    // store the File object
// Global tracker for all uploaded files and their numbers
const uploadedNumbersMap = new Map();
let fileUploadCounter = 0; // Counter to help track files
function initFancyUploader() {
    const $input = $('#numberUpload');

    // Destroy any existing instance
    if ($input.data('FancyFileUpload')) {
        $input.FancyFileUpload('destroy');
    }

    $input.FancyFileUpload({
        params: { action: 'fileuploader' },
        maxfilesize: 25 * 1024 * 1024,

        added(e, data) {
            const file = data.files[0];
            const ext = file.name.split('.').pop().toLowerCase();
            const $row = $('.ff_fileupload_row').last();
            const mode = $('input[name="fileMode"]:checked').val();
            const isCustom = mode === 'custom';
            // and the prefix from the same checked input
            const columnPrefix = $('input[name="fileMode"]:checked')
                .data('column-prefix') || '';
            const uniqueId = `${file.name}_${Date.now()}`;
            lastFile = file;
            lastFileData = data;
            if (isCustom && (ext === 'xls' || ext === 'xlsx')) {
                lastFile = file;
                lastFileData = data;
                buildCustomSelectors(file, data);
                return false;
            }
            if (ext === 'xls' || ext === 'xlsx') {
                previewExcelRows(file);
            }
            // Standard mode: just call doUpload immediately
            if (!isCustom) {
                const uid = `${file.name}_${Date.now()}`;
                doUpload(data, file, uniqueId, null, null);
                return false;
            }

            // Custom mode: fetch column names
            const fd = new FormData();
            fd.append('file', file);
            $.ajax({
                url: '/Home/GetColumns',
                type: 'POST',
                data: fd,
                contentType: false,
                processData: false,
                dataType: 'json'
            }).done(cols => {
                const columns = Array.isArray(cols)
                    ? cols
                    : (Array.isArray(cols.$values) ? cols.$values : (cols.d || []));
                //  const columnPrefix = $('#fileMode').data('column-prefix') || ''; 
                $('#nameSelectors, #numberSelectors').show();
                // Populate Name buttons
                const $nb = $('#nameButtons').empty();
                columns.forEach(colKey => {
                    // extract the “A”, “B”, “C” suffix:
                    const letter = colKey.split('_')[1];
                    const displayText = `${columnPrefix}${letter}`;
                    $('<button type="button" class="btn btn-outline-secondary me-2 mb-2">')
                        .text(`${columnPrefix}_${letter}`)     // e.g. “Sütun B”
                        .data('col', colKey)                  // store the raw key: “Column_B”
                        .on('click', function () {
                            $nb.find('button').removeClass('active').prop('disabled', false);
                            $(this).addClass('active');
                            $('#SelectedCustomColumnKey').val(colKey);
                            $('#SelectedCustomColumn').val(`${columnPrefix}_${letter}`);
                            // $('#SelectedCustomColumn').val($(this).text());
                            $('#numberButtons button')
                                .prop('disabled', false)                        // reset all
                                .filter((_, btn) => $(btn).data('col') === colKey)
                                .prop('disabled', true);
                            insertPlaceholder(`${columnPrefix}_${letter}`);
                            maybeAutoUpload();
                        })
                        .appendTo($nb);
                });

                // Number buttons
                const $numB = $('#numberButtons').empty();
                columns.forEach(colKey => {
                    const letter = colKey.split('_')[1];
                    $('<button type="button" class="btn btn-outline-secondary me-2 mb-2">')
                        .text(`${columnPrefix}_${letter}`)
                        .data('col', colKey)
                        .on('click', function () {
                            $numB.find('button').removeClass('active').prop('disabled', false);
                            $(this).addClass('active');
                            $('#SelectedNumberColumnKey').val(colKey);
                            $('#SelectedNumberColumn').val(`${columnPrefix}_${letter}`);
                            $('#nameButtons button')
                                .prop('disabled', false)
                                .filter((_, btn) => $(btn).data('col') === colKey)
                                .prop('disabled', true);
                            loadNumbersForColumn(file, $(this).data('col'));
                            maybeAutoUpload();
                        })
                        .appendTo($numB);
                });
                function maybeAutoUpload() {
                    const nameCol = $('#SelectedCustomColumnKey').val();
                    const numberCol = $('#SelectedNumberColumnKey').val();
                    if (nameCol && numberCol) {
                        doUpload(data, file, `${file.name}_${Date.now()}`, nameCol, numberCol);
                        // Optionally, hide the selectors or disable buttons
                        //  $('#nameSelectors,#numberSelectors').hide();
                    }
                }
                // Show the selectors
                $('#nameSelectors, #numberSelectors').show();

                // Create and bind the Upload button **right here**
                const $uploadBtn = $('<button type="button" class="btn btn-sm btn-primary">Upload</button>');
                $uploadBtn.on('click', ev => {
                    ev.preventDefault();
                    const nameCol = $('#SelectedCustomColumn').val();
                    const numberCol = $('#SelectedNumberColumn').val();
                    console.log('doUpload called with:', uniqueId, nameCol, numberCol);
                    doUpload(data, file, uniqueId, nameCol, numberCol);
                });

                // Insert it into the same row
                $row
                    .find('.ff_custom_upload_btn').remove() // clean up old
                    .end()
                    .append($('<div class="mt-2 ff_custom_upload_btn">').append($uploadBtn));
            });

            // Always return false so the plugin does _not_ auto‑upload
            return false;
        }
    });
}


function buildCustomSelectors(file, data) {
    lastFile = file;
    lastData = data;
    const prefix = $('input[name="fileMode"]:checked').data('column-prefix') || '';
    const fd = new FormData(); fd.append('file', file);

    $.ajax({
        url: '/Home/GetColumns',
        type: 'POST', data: fd, contentType: false, processData: false, dataType: 'json'
    }).done(cols => {
        const columns = Array.isArray(cols)
            ? cols
            : Array.isArray(cols.$values)
                ? cols.$values
                : (cols.d || []);

        $('#nameButtons,#numberButtons').empty();
        $('#nameSelectors,#numberSelectors').show();

        columns.forEach(colKey => {
            const letter = colKey.split('_')[1];
            const label = `${prefix}_${letter}`;

            // Name button
            $('<button type="button" class="btn btn-outline-secondary me-2 mb-2">')
                .text(label)
                .data('col', colKey)
                .on('click', function () {
                    $('#nameButtons button').removeClass('active').prop('disabled', false);
                    $(this).addClass('active');
                    $('#SelectedCustomColumnKey').val(colKey);
                    $('#SelectedCustomColumn').val(label);
                    // disable matching number
                    $('#numberButtons button')
                        .prop('disabled', false)
                        .filter((_, b) => $(b).data('col') === colKey)
                        .prop('disabled', true);
                    insertPlaceholder(label);
                    maybeAutoUpload();
                })
                .appendTo('#nameButtons');

            // Number button (same pattern)
            $('<button type="button" class="btn btn-outline-secondary me-2 mb-2">')
                .text(label)
                .data('col', colKey)
                .on('click', function () {
                    $('#numberButtons button').removeClass('active').prop('disabled', false);
                    $(this).addClass('active');
                    $('#SelectedNumberColumnKey').val(colKey);
                    $('#SelectedNumberColumn').val(label);
                    $('#nameButtons button')
                        .prop('disabled', false)
                        .filter((_, b) => $(b).data('col') === colKey)
                        .prop('disabled', true);
                    loadNumbersForColumn(file, colKey);
                    maybeAutoUpload();
                })
                .appendTo('#numberButtons');
        });

        // on‐demand reupload
        const maybeAutoUpload = () => {
            const n = $('#SelectedCustomColumnKey').val();
            const m = $('#SelectedNumberColumnKey').val();
            if (n && m) {
                doUpload(data, file, `${file.name}_${Date.now()}`, n, m);
            }
        };
    });
}

function previewExcelRows(file) {
    const reader = new FileReader();
    reader.readAsArrayBuffer(file);
    reader.onload = evt => {
        const data = new Uint8Array(evt.target.result);
        const wb = XLSX.read(data, { type: 'array' });
        const rows = XLSX.utils.sheet_to_json(wb.Sheets[wb.SheetNames[0]], { header: 1 });
        const preview = rows.slice(0, 3);

        if (preview.length === 0) {
            $('#filePreview').html('<p>No data found.</p>');
            return;
        }

        let html = '<table class="table table-sm table-bordered"><thead><tr>';
        preview[0].forEach(c => html += `<th>${c || ''}</th>`);
        html += '</tr></thead><tbody>';
        preview.slice(1).forEach(r => {
            html += '<tr>';
            r.forEach(c => html += `<td>${c || ''}</td>`);
            html += '</tr>';
        });
        html += '</tbody></table>';

        $('#filePreview').html(html);
    };
}
//$(document).on('click', '.ff_custom_upload_btn', function (ev) {
//    ev.preventDefault();
//    const $btn = $(this);
//    const { fancyData, file, uniqueId } = $btn.data();

//    // grab the two columns from your hidden fields
//    const nameCol = $('#SelectedCustomColumn').val();
//    const numberCol = $('#SelectedNumberColumn').val();

//    doUpload(fancyData, file, uniqueId, nameCol, numberCol);
//    return false;  // prevent any other handler
//});
function insertPlaceholder(col) {
    const $msg = $('#message');
    const txt = $msg.val();
    const pos = $msg.prop('selectionStart') || txt.length;
    const ph = `{${col}}`;
    $msg.val(txt.slice(0, pos) + ph + txt.slice(pos));
    $msg[0].selectionStart = $msg[0].selectionEnd = pos + ph.length;
    $msg.focus();
}
let suppressMapUpdate = false;
function loadNumbersForColumn(file, col) {
    const fd = new FormData();
    fd.append('file', file);
    fd.append('columnName', col);
    $.ajax({
        url: '/Home/GetColumnValues',
        type: 'POST',
        data: fd,
        contentType: false,
        processData: false,
        dataType: 'json'
    }).done(numbers => {
        const vals = Array.isArray(numbers)
            ? numbers
            : (Array.isArray(numbers.$values) ? numbers.$values : []);

        // now vals is a true Array, so .join works
        $('#PhoneNumbers').val(vals.join(',\n'));
        $('#PhoneNumbers').trigger('input');
        suppressMapUpdate = false;
        // optionally trigger your existing validator/summary updater here
    });
}
function doUpload(data, file, uniqueId, nameCol, numberCol) {
    const fd = new FormData();
    fd.append('files', file);

    let url = nameCol && numberCol
        ? '/Home/UploadCustomRecipients'
        : '/Home/UploadNumbers';

    if (nameCol && numberCol) {
        fd.append('file', file);
        fd.append('nameColumn', nameCol);
        fd.append('numberColumn', numberCol);
        url = '/Home/UploadCustomRecipients';
    }
    else {
        // your existing standard endpoint
        fd.append('files', file);
        url = '/Home/UploadNumbers';
    }


    $.ajax({
        url,
        type: 'POST',
        data: fd,
        contentType: false,
        processData: false,
        dataType: 'json'
    }).done(res => {
        let recs = [];

        if (Array.isArray(res.records)) {
            recs = res.records.map(r => ({
                Name: r.name ?? r.Name,
                Number: r.number ?? r.Number
            }));
        }
        else if (res.numbers) {
            // handle top‑level numbers array or numbers.$values
            const nums = Array.isArray(res.numbers)
                ? res.numbers
                : Array.isArray(res.numbers.$values)
                    ? res.numbers.$values
                    : [];
            recs = nums.map(n => ({ Name: '', Number: n }));
        }

        // store the full Name/Number array for your form post
        $('#RecipientsJson').val(JSON.stringify(recs));

        const $row = $('.ff_fileupload_queued').last();
        $row.attr('data-file-id', uniqueId);
        $row.find('.ff_fileupload_remove_file').attr('data-file-id', uniqueId);
        const nums = recs.map(r => r.Number);
        // 4) store in your map and update textarea
        uploadedNumbersMap.set(uniqueId, { numbers: nums });
        updateTextareaFromMap();
    });
}

$(document).on('click', '.ff_fileupload_remove_file', function () {
    const fileId = $(this).data('file-id');

    if (fileId && uploadedNumbersMap.has(fileId)) {
        uploadedNumbersMap.delete(fileId);
        updateTextareaFromMap();
    }
});
$(document).ready(function () {
    bindGlobalPasswordGenerators();
    // --- Reset form on modal hide ---
    $('#smsModal').on('hidden.bs.modal', function () {
        $('#smsForm')[0].reset();
        $('#smsForm select').each(function () {
            if ($(this).hasClass('select2-hidden-accessible')) {
                $(this).val(null).trigger('change');
            }
        });
        $('#numberUpload').FancyFileUpload('reset');
        lastFile = null;
        lastFileData = null;
        // Clear the uploaded numbers map and textarea
        uploadedNumbersMap.clear();
        $('#PhoneNumbers').val('');
        const $dependentInputs = $('.dependent-fields').find('input, select, textarea, button');
        $dependentInputs.prop('disabled', true).addClass('disabled');
        $('#smsCount').text("Total Numbers: 0");
        $('#validCount').text("✅ Valid: 0");
        $('#invalidCount').text("❌ Invalid: 0");
    });
    let lastValue = '';

    document.querySelectorAll('.phoneNumberinput').forEach(function (inputEl) {
        inputEl.addEventListener('input', function (e) {
            const currentValue = e.target.value;
            const isDeleting = currentValue.length < lastValue.length;

            let input = currentValue.replace(/\D/g, ''); // remove non-digits

            // Ensure it starts with '5'
            if (input.length > 0 && !input.startsWith('5')) {
                input = '5' + input.replace(/^5+/, '');
            }

            // Format the number
            let formatted = input;
            if (input.length > 3) {
                formatted = '(' + input.slice(0, 3) + ') ' + input.slice(3);
            }
            if (input.length > 6) {
                formatted = formatted.slice(0, 9) + '-' + input.slice(6);
            }

            if (!isDeleting || formatted.length !== currentValue.length) {
                e.target.value = formatted;
            }

            lastValue = e.target.value;
        });
    });
    $('#markAllAsRead').on('click', function () {
        $.ajax({
            type: 'POST',
            url: '/Notifications/MarkAllAsRead',
            success: function (response) {
                if (response.success) {
                    loadNotifications(); // Refresh notification list
                }
            }
        });
    });
    $('#profileForm').on('submit', function (e) {
        e.preventDefault();

        var formData = new FormData(this);

        $.ajax({
            url: '/Users/UpdateProfile',
            type: 'POST',
            data: formData,
            processData: false, // Prevent jQuery from processing the data
            contentType: false, // Prevent jQuery from setting content type
            success: function (response) {
                if (response.success) {
                    toastr.success(window.localizedTextDT.profileupdatedsuccess || 'Profile updated successfully!');

                    // 🔄 Refresh the page after a short delay (e.g., 1.5s)
                    setTimeout(function () {
                        location.reload();
                    }, 1500);
                } else {
                    toastr.error(response.message.value || 'Güncelleme başarısız oldu.');
                }
            },
            error: function (xhr) {
                // Show backend model errors if any
                let errMsg = xhr.responseJSON?.message || 'Güncelleme başarısız oldu.';
                toastr.error(errMsg);
            }
        });
    });
    $.extend(true, $.fn.dataTable.defaults, {
        language: {
            search: window.localizedTextDT.search,
            info: window.localizedTextDT.info,
            infoEmpty: window.localizedTextDT.infoEmpty,
            infoFiltered: window.localizedTextDT.infoFiltered,
            lengthMenu: window.localizedTextDT.lengthMenu,
            processing: window.localizedTextDT.processing,
            loadingRecords: window.localizedTextDT.loadingRecords,
            zeroRecords: window.localizedTextDT.noRecords,
            emptyTable: window.localizedTextDT.noData,
            paginate: {
                first: window.localizedTextDT.first,
                last: window.localizedTextDT.last,
                next: window.localizedTextDT.next,
                previous: window.localizedTextDT.previous
            },
            buttons: {
                copy: window.localizedTextDT.copy,
                excel: window.localizedTextDT.excel,
                pdf: window.localizedTextDT.pdf,
                print: window.localizedTextDT.print
            }
        }
    });
    $(document).on('click', '.resend-order-btn', function () {
        var orderId = $(this).data('order-id');

        if (confirm(window.localizedTextDT.resendorder || 'Are you sure you want to resend this order?')) {
            $.ajax({
                type: 'POST',
                url: window.resendUrl,
                data: { orderId: orderId },
                success: function (response) {
                    alert(response.message.value);
                    // Reload orders DataTable after resend
                    $('#ordersList').DataTable().ajax.reload(null, false);
                },
                error: function (xhr) {
                    alert(window.localizedTextDT.failed || 'Failed: ' + xhr.responseText);
                }
            });
        }
    });
    $('#directoriesTable').DataTable({
        "language": {
            "emptyTable": "No directories found."
        }
    });

});

//$('#selectFirm').on('change', function () {
//    const selectedOption = this.options[this.selectedIndex];
//    const credit = selectedOption ? selectedOption.getAttribute('data-credit') || 0 : 0;

//    // Update hidden + visible fields
//    document.getElementById('AvailableCredit').value = credit;
//    document.getElementById('availableCredit').innerText = parseFloat(credit).toLocaleString('en-US', {
//        minimumFractionDigits: 2,
//        maximumFractionDigits: 2
//    });

//    // Enable dependent fields
//    const selected = parseInt($(this).val());
//    const isValid = !isNaN(selected) && selected > 0;

//    $dependentInputs.prop('disabled', !isValid).toggleClass('disabled', !isValid);
//    $('#numberUpload').closest('.fancy-file-upload').toggleClass('disabled', !isValid);

//    // Recalculate
//    // updateNumbersCount();
//});
// --- Show/Hide password toggle ---
$("#show_hide_password a").on('click', function (e) {
    e.preventDefault();

    var $input = $('#show_hide_password input');
    var $icon = $(this).find('i'); // ✅ only target the eye icon inside the <a>

    if ($input.attr("type") === "text") {
        $input.attr('type', 'password');
        $icon.removeClass("bi-eye-fill").addClass("bi-eye-slash-fill");
    } else {
        $input.attr('type', 'text');
        $icon.removeClass("bi-eye-slash-fill").addClass("bi-eye-fill");
    }
});

function initFlatpickr() {
    const turkeyTime = luxon.DateTime.now().setZone("Europe/Istanbul");

    $(".date-time").flatpickr({
        enableTime: true,
        dateFormat: "Y-m-d H:i",
        minDate: "today",
        time_24hr: true,
        defaultHour: turkeyTime.hour,
        defaultMinute: turkeyTime.minute,
        minuteIncrement: 1
    });
}
$(document).ready(function () {

    // --- Enable on firm select ---

    //   $("#loginForm").submit(function (e) {
    //    e.preventDefault(); // 🚀 Stop normal POST (no refresh)

    //    var formData = {
    //        username: $("#inputUsername").val(),
    //        password: $("#inputChoosePassword").val()
    //    };

    //    $.ajax({
    //        url: "/Account/Login", 
    //        method: "POST",
    //        data: formData,
    //        success: function (response) {
    //            // 🚀 If login success → redirect to Home
    //            window.location.href = "/Home/Index";
    //        },
    //        error: function (xhr) {
    //            // 🚀 If login failed → show error message
    //            var errorText = xhr.responseText || "Bir hata oluştu.";
    //            $("#loginError").html('<div class="alert alert-danger text-center">' + errorText + '</div>');
    //        }
    //    });
    //});
    // --- Select2 Dropdowns ---
    $('#selectDirectory, #CompanyId').select2({
        theme: "bootstrap-5",
        width: '100%',
        placeholder: function () {
            return $(this).data('placeholder');
        },
        allowClear: true
    });

    $('#currencyCode, #chooseApi').select2({
        theme: "bootstrap-5",
        width: '100%',
        placeholder: function () {
            return $(this).data('placeholder');
        },
        allowClear: true
    });

    loadTodaySmsStats();

    setInterval(loadTodaySmsStats, 60000);


    loadDashboardStats();






    // --- Company Add Form ---
    $('#addCompanyForm').on('submit', function (e) {
        e.preventDefault();

        const data = {
            CompanyName: $('#CompanyName').val(),
            IsTrustedSender: $('#IsTrustedSender').is(':checked'),
            IsRefundable: $('#IsRefundable').is(':checked'),
            CanSendSupportRequest: $('#CanSendSupportRequest').is(':checked'),
            Apid: parseInt($("#Apid").val()),
            CurrencyCode: $('#CurrencyCode').val(),
            LowPrice: parseFloat($('#LowPrice').val()),
            MediumPrice: parseFloat($('#MediumPrice').val()),
            HighPrice: parseFloat($('#HighPrice').val()),
            FullName: $('#FullName').val(),
            UserName: $('#UserName').val(),
            Email: $('#Email').val(),
            Phone: $('#Phone').val(),
            Password: $('#Password').val()
        };

        if (!data.CompanyName || !data.FullName || !data.Email || !data.UserName || !data.Password) {
            alert(window.localizedTextDT.fillAllRequiredFields || "Please fill all required fields.");
            return;
        }

        $.ajax({
            url: '/Companies/Add',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(data),
            success: function (response) {
                if (response.success) {
                    $('#addCompanyModal').modal('hide');
                    $('#addCompanyForm')[0].reset();
                    toastr.success(window.localizedTextDT.Companyadded || "Company added successfully!");
                    loadCompanies();
                }
            },
            error: function (xhr) {
                console.error(xhr);
                toastr.error(window.localizedTextDT.ErrorForm || "Error while submitting form.");
            }
        });
    });

    // --- Load Companies Table ---
    loadCompanies();

    const $table = $('#usersTable');
    if ($table.length && !$.fn.DataTable.isDataTable($table)) {
        const table = $table.DataTable({
            lengthChange: false,
            buttons: ['copy', 'excel', 'pdf', 'print']
        });

        table.buttons().container().appendTo('#usersTable_wrapper .col-md-6:eq(0)');
    }

    const $rolestable = $('#rolesTable');
    if ($rolestable.find('tbody tr').length > 0) {
        const rolestable = $rolestable.DataTable({
            lengthChange: false,
            buttons: ['copy', 'excel', 'pdf', 'print']
        });

        rolestable.buttons().container().appendTo('#rolesTable_wrapper .col-md-6:eq(0)');
    }

    $('#manualBtn').on('click', function () {
        $('#unitPrice').prop('disabled', false).val('');
        $('#unitPriceHidden').val('');
    });
    $('#IsReadOnly').on('change', function () {
        const isChecked = $(this).is(':checked');

        // Check all checkboxes where value ends with "_Read"
        $('input[type="checkbox"][name="SelectedPermissions"]').each(function () {
            if ($(this).val().endsWith('_Read')) {
                $(this).prop('checked', isChecked);
            }
        });
    });

    // Handle mutual exclusivity between file upload and manual textarea entry
    $('#numberUpload').on('change', function () {
        if (this.files.length > 0) {
            $('#PhoneNumbers').prop('disabled', true).val('');
            updateTextareaFromMap();
        } else {
            $('#PhoneNumbers').prop('disabled', false);
        }
    });

    $('#PhoneNumbers').on('input', function () {
        if ($(this).val().trim().length > 0) {
            $('#numberUpload').prop('disabled', true);
        } else {
            $('#numberUpload').prop('disabled', false);
        }
    });

    // Prevent file upload button from submitting parent form
    $(document).on('click', '.ff_fileupload_start_upload', function (e) {
        e.preventDefault();
        e.stopPropagation();
    });
    $('#openAddCreditModal').click(function () {
        updateUnitPriceOptionsFromInputs();
        var modal = new bootstrap.Modal(document.getElementById('addCreditModal'));
        modal.show();
    });
    function recalculateLoan() {
        var priceText = ($("#priceInput").val() || "").replace(",", ".");
        var unitPriceInput = ($("#unitPrice").val() || "").replace(",", ".");

        var price = parseFloat(priceText);
        var unitPrice = parseFloat(unitPriceInput);
        $("#unitPriceHidden").val(isNaN(unitPrice) ? "" : unitPrice.toFixed(4));

        if (isNaN(price) || price <= 0) {
            $("#loanDisplay").val("");
            return;
        }

        if (isNaN(unitPrice) || unitPrice <= 0) {
            $("#loanDisplay").val("");
            return;
        }

        var creditValue = price / unitPrice;
        var formattedCredit = Math.floor(creditValue).toLocaleString("en-US");

        $("#loanDisplay").val(formattedCredit);
    }
    // Modal shown event
    $('#addCreditModal').on('shown.bs.modal', function () {
        const $modal = $(this);

        // Get dynamic values from main form
        const low = $('#lowPriceInput').val()?.replace(',', '.');
        const mid = $('#mediumPriceInput').val()?.replace(',', '.');
        const high = $('#highPriceInput').val()?.replace(',', '.');

        const parsedPrices = [low, mid, high]
            .map(p => parseFloat(p))
            .filter(p => !isNaN(p) && p > 0);

        const uniquePrices = [...new Set(parsedPrices)].sort((a, b) => a - b);

        const $container = $modal.find('#unitPriceOptionsContainer');
        $container.empty();

        // Dynamically build price buttons
        uniquePrices.forEach(price => {
            const btn = $(`<button type="button" class="btn btn-outline-primary me-1 unit-price-btn">${price.toFixed(2).replace('.', ',')}</button>`);
            btn.on('click', function () {
                $('#unitPrice').val(price.toFixed(2).replace('.', ',')).prop('disabled', true);
                $('#unitPriceHidden').val(price.toFixed(4));
                $('#priceMode').val('automatic');
                $('.unit-price-btn').removeClass('btn-primary').addClass('btn-outline-primary');
                $(this).removeClass('btn-outline-primary').addClass('btn-primary');
                $('#manualPriceBtn').removeClass('btn-success').addClass('btn-outline-secondary');
                recalculateLoan();
            });
            $container.append(btn);
        });

        // Manual button
        const manualBtn = $('<button type="button" id="manualPriceBtn" class="btn btn-outline-secondary">Manual</button>');
        manualBtn.on('click', function () {
            $('#unitPrice').prop('disabled', false).focus();
            $('#priceMode').val('manual');
            $('#unitPriceHidden').val(''); // clear previous automatic value
            $('.unit-price-btn').removeClass('btn-primary').addClass('btn-outline-primary');
            $(this).removeClass('btn-outline-secondary').addClass('btn-success');
            recalculateLoan();
        });
        $container.append(manualBtn);

        // Default selection
        if (uniquePrices.length === 1) {
            $('#unitPrice').val(uniquePrices[0].toFixed(2).replace('.', ',')).prop('disabled', true);
            $('#unitPriceHidden').val(uniquePrices[0].toFixed(4));
        } else {
            $('#unitPrice').val('').prop('disabled', true);
            $('#unitPriceHidden').val('');
        }

        // Initial loan calculation
        recalculateLoan();

        // Sync hidden field in manual input
        $("#priceInput, #unitPrice").off("input").on("input", function () {
            if ($('#priceMode').val() === 'manual') {
                const manualVal = $('#unitPrice').val()?.replace(',', '.');
                const parsed = parseFloat(manualVal);
                if (!isNaN(parsed)) {
                    $('#unitPriceHidden').val(parsed.toFixed(4));
                } else {
                    $('#unitPriceHidden').val('');
                }
            }
            recalculateLoan();
        });
        $('#priceInput').off('input.autoPick').on('input.autoPick', function () {
            if ($('#priceMode').val() === 'manual') return;

            const raw = $(this).val().replace(',', '.');
            const price = parseFloat(raw);
            let tier = null;

            if (price >= 1_000_000) tier = 0.19;
            else if (price >= 500_000) tier = 0.20;
            else tier = 0.21;

            if (tier === null) return;

            // Find button by data-price attribute (exact match)
            const $targetBtn = $('#unitPriceOptions .unit-price-option')
                .filter((_, btn) => parseFloat($(btn).data('price')) === tier)
                .first();

            if ($targetBtn.length) {
                $targetBtn.trigger('click');
            }
        });
    });
    function updateUnitPriceOptionsFromInputs() {
        const prices = new Set();

        const low = parseFloat($("#lowPriceInput").val().replace(',', '.'));
        const medium = parseFloat($("#mediumPriceInput").val().replace(',', '.'));
        const high = parseFloat($("#highPriceInput").val().replace(',', '.'));

        if (!isNaN(low)) prices.add(low.toFixed(2));
        if (!isNaN(medium)) prices.add(medium.toFixed(2));
        if (!isNaN(high)) prices.add(high.toFixed(2));

        const container = $("#unitPriceOptions");
        container.empty();

        prices.forEach(price => {
            const btn = `<button type="button" class="btn btn-outline-success unit-price-option" data-price="${price}">${price}</button>`;
            container.append(btn);
        });

        // Manual button
        container.append(`<button type="button" class="btn btn-outline-secondary" id="manualBtn">Manual</button>`);

        bindUnitPriceButtons(); // Rebind events
    }
    function bindUnitPriceButtons() {
        $(".unit-price-option").off("click").on("click", function () {
            const selected = $(this).data("price");

            $("#unitPrice")
                .val(selected)
                .prop("readonly", true);
            $("#unitPriceHidden").val(parseFloat(selected).toFixed(4));

            recalculateLoan();

            $(".unit-price-option").removeClass("active");
            $(this).addClass("active");

            $("#manualBtn")
                .removeClass("active btn-outline-success")
                .addClass("btn-outline-secondary");

            $("#priceMode").val("automatic");
        });

        $("#manualBtn").off("click").on("click", function () {
            $("#priceMode").val("manual");

            // Enable editing
            $("#unitPrice").prop("disabled", false).prop("readonly", false).focus();

            // Style toggling
            $(this).addClass("active").removeClass("btn-outline-secondary").addClass("btn-outline-success");
            $(".unit-price-option, .unit-price-btn").removeClass("btn-primary").addClass("btn-outline-primary");

            recalculateLoan();
        });
    }
    function getLocalizedButtons() {
        return [
            {
                extend: 'copy',
                text: window.localizedTextDT.copy,
            },
            {
                extend: 'excel',
                text: window.localizedTextDT.excel,
            },
            {
                extend: 'pdf',
                text: window.localizedTextDT.pdf,
            },
            {
                extend: 'print',
                text: window.localizedTextDT.print,
            }
        ];
    }
    const isAdminBool = window.currentUserType === 'Admin';
    const isCompany = window.currentUserType === 'CompanyUser';
    const canReadOrders = window.canReadOrder === 'True';
    const canEditOrders = window.canEditOrder === 'True';
    const canChangeApi = isAdminBool || (!isCompany && canEditOrders);
    const adminColumns = [
        // Buttons
        {
            data: null,
            orderable: false,
            defaultContent: '',
            render: function (data, type, row) {
                const status = row.status;
                const orderId = row.orderId;
                let buttonsHtml = '';

                if (status === "AwaitingApproval") {
                    if (canChangeApi) {
                        const confirmText = document.getElementById('confirmText')?.dataset?.value || 'Approve';
                        buttonsHtml += `<button class="btn btn-outline-warning confirm-sms-btn mb-1" data-id="${orderId}">${confirmText}</button>`;
                    }
                } else {
                    if (["Waiting to be sent", "WaitingToBeSent"].includes(status)) {
                        buttonsHtml += `<button class="btn btn-outline-primary confirm-sms-btn mb-1" data-id="${orderId}">${window.localizedTextDT?.confirmsend || 'Confirm Send'}</button>`;
                    }
                    if (status === "Failed") {
                        buttonsHtml += `<button class="btn btn-outline-warning resend-order-btn" data-order-id="${orderId}">${window.localizedTextDT?.resend || 'Resend'}</button>`;
                    }
                }

                return `<div class="d-flex flex-column gap-1">${buttonsHtml}</div>`;
            }
        },
        {
            data: 'orderId',
            orderable: true,
            render: {
                display: function (data, type, row) {
                    const orderLink = `<a href="#" class="order-details-toggle" data-id="${row.orderId}">${row.orderId}</a>`;
                    let actionHtml = '';
                    const status = row.status;

                    if (["Waiting to be sent", "WaitingToBeSent", "Scheduled", "Failed", "AwaitingApproval"].includes(status)) {
                        actionHtml += `
                        <div class="dropdown d-inline-block">
                            <button class="btn btn-sm btn-icon dropdown-toggle" type="button" data-bs-toggle="dropdown">
                                <i class="bi bi-three-dots-vertical"></i>
                            </button>
                             <ul class="dropdown-menu">
                        ${canChangeApi ? `<li><a class="dropdown-item change-sms-service" data-id="${row.orderId}" href="#">${window.localizedTextDT?.changeApi || 'Change API'}</a></li>` : ''}
                        ${canEditOrders ? `<li><a class="dropdown-item cancel-order" data-id="${row.orderId}" href="#">${window.localizedTextDT?.cancelorder || 'Cancel Order'}</a></li>` : ''}
                    </ul>
                        </div>`;
                    }

                    return `<div class="d-flex justify-content-between align-items-center" style="gap: 10px;">
                    <div>${orderLink}</div>
                    <div class="d-flex align-items-center gap-2">${actionHtml}</div>
                </div>`;
                },
                sort: (data, type, row) => row.orderId,
                filter: (data, type, row) => row.orderId
            }
        },
        { data: 'status', render: (data) => formatStatus(data) },
        { data: 'companyName' },
        {
            data: 'dateOfSending', render: function (data, type, row) {
                let dateString = row.dateOfSending;

                // Use scheduledSendDate if status is Scheduled
                if (row.status === 'Scheduled' && row.scheduledSendDate) {
                    dateString = row.scheduledSendDate;
                }

                if (!dateString) return ''; // Handle missing date

                const date = new Date(dateString);
                const formatted = `${String(date.getDate()).padStart(2, '0')}/${String(date.getMonth() + 1).padStart(2, '0')}/${date.getFullYear()} ${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;

                // Optional visual highlight for scheduled orders
                return row.status === 'Scheduled'
                    ? `<span class="badge bg-warning text-dark">${formatted}</span>`
                    : formatted;
            }
        },
        {
            data: 'createdAt', render: data => {
                if (!data) return '';
                const date = new Date(data);
                return `${String(date.getDate()).padStart(2, '0')}/${String(date.getMonth() + 1).padStart(2, '0')}/${date.getFullYear()} ${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
            }
        },
        { data: 'apiName' },
        { data: 'submissionType' },
        { data: 'loadedCount' },
        { data: 'deliveredCount' },
        { data: 'unsuccessfulCount' },
        { data: 'createdBy' },
        { data: 'refundable', render: data => data ? 'Yes' : 'No' },
        { data: 'returned', render: data => data ? 'Yes' : 'No' },
        { data: 'returnDate', render: formatDate }
    ];

    const nonAdminColumns = [
        { data: null, orderable: false, defaultContent: '', render: adminColumns[0].render },
        { data: 'orderId', orderable: true, render: adminColumns[1].render },
        { data: 'status', render: adminColumns[2].render },
        {
            data: 'dateOfSending', render: function (data, type, row) {
                let dateString = row.dateOfSending;

                // Use scheduledSendDate if status is Scheduled
                if (row.status === 'Scheduled' && row.scheduledSendDate) {
                    dateString = row.scheduledSendDate;
                }

                if (!dateString) return ''; // Handle missing date

                const date = new Date(dateString);
                const formatted = `${String(date.getDate()).padStart(2, '0')}/${String(date.getMonth() + 1).padStart(2, '0')}/${date.getFullYear()} ${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;

                // Optional visual highlight for scheduled orders
                return row.status === 'Scheduled'
                    ? `<span class="badge bg-warning text-dark">${formatted}</span>`
                    : formatted;
            }
        },
        {
            data: 'createdAt', render: data => {
                if (!data) return '';
                const date = new Date(data);
                return `${String(date.getDate()).padStart(2, '0')}/${String(date.getMonth() + 1).padStart(2, '0')}/${date.getFullYear()} ${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
            }
        },
        { data: 'deliveredCount' },
        { data: 'createdBy' }
    ];
    const $ordersTable = $('#ordersList');

    if ($ordersTable.length > 0) {
        if ($.fn.DataTable.isDataTable($ordersTable)) {
            $ordersTable.DataTable().clear().destroy();
        }
        const table = $ordersTable.DataTable({
            ajax: {
                url: window.ordersControllerUrl || '/Orders/GetAllOrders',
                data: function (d) {
                    const statusFilter = $('#ordersList .filter-row select[data-column="2"]').val();
                    if (statusFilter && statusFilter !== '' && statusFilter !== 'all') {
                        d.status = statusFilter;
                    }
                    return d;
                },
                dataSrc: function (json) {
                    return json?.$values || []; // Correctly returns the array
                }
            },
            pagingType: 'full_numbers',
            language: {
                paginate: {
                    first: '«',
                    last: '»',
                    next: '›',
                    previous: '‹'
                }
            },
            order: [[1, 'desc']],
            columns: isAdminBool ? adminColumns : nonAdminColumns,
            initComplete: function () {
                this.api().order([1, 'desc']).draw();
            },
            destroy: true,
            lengthChange: true,
            lengthMenu: [[10, 25, 50, 100], [10, 25, 50, 100]],

            dom: '<"top"Bf><"table-responsive"rt><"bottom d-flex justify-content-between align-items-center"ilp>',
            buttons: getLocalizedButtons()
        });
        const companyId = parseInt(
            document.querySelector('meta[name="company-id"]').content,
            10
        );
        const isAdmin = document.querySelector('meta[name="is-admin"]').content === 'true';

        // build your SignalR connection
        const orderHub = new signalR.HubConnectionBuilder()
            .withUrl('/chathub')
            .build();

        // wire up your two handlers (you already have these)
        orderHub.on('ReceiveNewOrder', order => {
            const normalized = {
                orderId: order.orderId || order.OrderId || 0,
                status: order.status || order.Status || '',
                companyName: order.companyName || order.CompanyName || '',
                apiName: order.apiName || order.ApiName || '',
                submissionType: order.submissionType || order.SubmissionType || '',
                loadedCount: order.loadedCount ?? order.LoadedCount ?? 0,
                deliveredCount: order.deliveredCount ?? order.DeliveredCount ?? 0,
                unsuccessfulCount: order.unsuccessfulCount ?? order.UnsuccessfulCount ?? 0,
                createdBy: order.createdBy || order.CreatedBy || '',
                dateOfSending: order.dateOfSending || order.DateOfSending || '',
                refundable: order.refundable ?? order.Refundable ?? false,
                returned: order.returned ?? order.Returned ?? false,
                returnDate: order.returnDate || order.ReturnDate || '',
                createdAt: order.createdAt || order.CreatedAt || ''
            };
            table.row.add(normalized).draw(false);
        });
        orderHub.on('OrderStatusChanged', data => {
            // find the row
            const idxs = table.rows((idx, rowData) => rowData.orderId === data.orderId).indexes();
            if (!idxs.length) return;

            // mutate the object
            const rowIdx = idxs[0];
            const rowData = table.row(rowIdx).data();
            rowData.status = data.newStatus;

            // tell DT to re-read & redraw
            table
                .row(rowIdx)
                .data(rowData)
                .invalidate()   // ⇐ important!
                .draw(false);
        });

        // start + join
        orderHub.start()
            .then(() => {
                console.log('SignalR connected');

                // company users join their company group
                if (companyId) {
                    orderHub.invoke('JoinCompanyGroup', companyId)
                        .catch(console.error);
                }

                // admins also join the "Admins" group
                if (isAdmin) {
                    orderHub.invoke('JoinAdminGroup')
                        .catch(console.error);
                }
                if (!isAdmin && canReadOrders) {
                    orderHub.invoke('JoinPanelGroup').catch(console.error);
                }
            })
            .catch(err => console.error('SignalR error:', err));
        function formatStatus(status) {
            switch (status) {
                case 'Scheduled':
                case 'WaitingToBeSent': return `<span class="badge bg-info">${window.localizedTextDT?.waiting_to_be_sent || status}</span>`;
                case 'Sent': return `<span class="badge bg-success">${window.localizedTextDT?.sent || status}</span>`;
                case 'Failed': return `<span class="badge bg-danger">${window.localizedTextDT?.failed || status}</span>`;
                case 'Cancelled': return `<span class="badge bg-dark">${window.localizedTextDT?.cancelled || status}</span>`;
                case 'AwaitingApproval': return `<span class="badge bg-warning text-dark">${window.localizedTextDT?.awaitingApproval || status}</span>`;
                default: return `<span class="badge bg-secondary">${status}</span>`;
            }
        }
        function formatDateTime(value) {
            if (!value) return '';
            const date = new Date(value);
            const formatted = `${String(date.getDate()).padStart(2, '0')}/${String(date.getMonth() + 1).padStart(2, '0')}/${date.getFullYear()} ${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
            return formatted;
        }
        const targetColumn = 10;

        document.querySelectorAll('.clear-date-filter').forEach(icon => {
            const col = parseInt(icon.dataset.column);
            if (!isNaN(col) && col === targetColumn) {
                icon.style.display = 'none';
            }
        });
        // Store table reference globally
        window.ordersTable = table;
        window.ordersDataTable = table;
        $(document).on('click', '.change-sms-service', function (e) {
            e.preventDefault();
            const orderId = $(this).data('id');
            $('#changeOrderId').val(orderId);

            // Fetch available APIs
            $.get(window.getApisUrl, function (response) {
                const apis = response.$values || []; // ✅ Extract array
                const $select = $('#newApiId');
                $select.empty();

                apis.forEach(api => {
                    $select.append(`<option value="${api.apiId}">${api.serviceName}</option>`);
                });

                $('#changeApiModal').modal('show');
            });
        });

        // Cancel order
        $(document).on('click', '.cancel-order', function (e) {
            e.preventDefault();
            const orderId = $(this).data('id');

            if (confirm(window.localizedTextDT?.cancelconfirmation || 'Are you sure you want to cancel this order?')) {
                $.post(`${window.ordersControllerUrl.replace('GetAllOrders', 'CancelOrder')}`, { orderId }, function (res) {
                    const msgKey = res.messageKey || 'DefaultSuccess';
                    toastr.success(window.localizedTextDT?.Ordercanceled || 'Order cancelled.');
                    $('#ordersList').DataTable().ajax.reload(null, false);
                }).fail(function (xhr) {
                    let msgKey = 'DefaultError';
                    try {
                        msgKey = JSON.parse(xhr.responseText)?.messageKey || msgKey;
                    } catch (e) { }
                    toastr.error(window.localizedTextDT?.OrderCannotBeCancelled || 'Error cancelling order.');
                });
            }
        });

        // ✅ FIXED: Status filter handler - single implementation
        $('#ordersList .filter-row select[data-column="2"]').off('change').on('change', function () {
            const selectedStatus = $(this).val();



            // 🔥 Instead of .search(''), do this:
            // table.clear().draw();

            // Then reload fresh:
            table.ajax.reload(function (json) {


                // Optional: verify after reload:
                $('#ordersList').DataTable().rows().every(function () {
                });
            }, false);
        });

        // ✅ FIXED: Other column filters (excluding status column)
        $('.column-filter').not('select[data-column="2"]').off('keyup change').on('keyup change', function () {
            const columnIndex = $(this).data('column');
            const value = this.value;

            // Show/hide clear icon for text inputs
            const clearIcon = $(this).siblings('.clear-filter');
            if (value && $(this).is('input[type="text"], input[type="number"]')) {
                clearIcon.show();
            } else {
                clearIcon.hide();
            }

            // Update header filter icon color
            const headerIcon = $('.header-filter-icon[data-column="' + columnIndex + '"]');
            if (value) {
                headerIcon.addClass('active-filter');
            } else {
                headerIcon.removeClass('active-filter');
            }

            // Apply client-side filtering for non-status columns
            if ($(this).is('input[type="number"]')) {
                table.column(columnIndex).search(value ? '^' + value + '.*' : '', true, false).draw();
            } else {
                table.column(columnIndex).search(value).draw();
            }

            checkIfAnyFilterApplied();
        });

        // Date range filtering for date columns (10, 13, 14)
        $('.date-filter').on('change', function () {
            const columnIndex = $(this).data('column');
            const startDate = $('.start-date[data-column="' + columnIndex + '"]').val();
            const endDate = $('.end-date[data-column="' + columnIndex + '"]').val();

            // Show/hide clear icon
            const clearIcon = $('.clear-date-filter[data-column="' + columnIndex + '"]');
            if (startDate || endDate) {
                clearIcon.show();
                $('.header-filter-icon[data-column="' + columnIndex + '"]').addClass('active-filter');
            } else {
                clearIcon.hide();
                $('.header-filter-icon[data-column="' + columnIndex + '"]').removeClass('active-filter');
            }

            // Apply date range filter
            filterByDateRange(columnIndex, startDate, endDate);
            checkIfAnyFilterApplied();
        });

        // Clear all filters
        $('#clearAllFilters').on('click', function () {
            // Clear all input filters
            $('.column-filter').val('');
            $('.clear-filter').hide();

            // Clear all date filters
            $('.date-filter').val('');
            $('.clear-date-filter').hide();

            // Clear status filter
            $('#ordersList .filter-row select[data-column="2"]').val('').trigger('change');

            // Clear all column searches
            table.columns().search('');

            // Clear custom date filters
            $.fn.dataTable.ext.search = [];

            // Reload table
            table.ajax.reload();

            // Reset filter styling
            $('.header-filter-icon').removeClass('active-filter');
            checkIfAnyFilterApplied();
        });

        // Date range filter function
        function filterByDateRange(columnIndex, startDate, endDate) {
            // Remove previous filters for this column
            $.fn.dataTable.ext.search = $.fn.dataTable.ext.search.filter(fn => fn.columnIndex !== columnIndex);

            // Handle single-date selection as exact match
            if (startDate && !endDate) endDate = startDate;
            if (!startDate && endDate) startDate = endDate;

            if (startDate || endDate) {
                $.fn.dataTable.ext.search.push(function (settings, data, dataIndex) {
                    const dateStr = data[columnIndex]; // e.g., "13/06/2025 09:37"
                    if (!dateStr) return false;

                    const parts = dateStr.split(/[\/\s:]/);
                    const day = parseInt(parts[0], 10);
                    const month = parseInt(parts[1], 10) - 1;
                    const year = parseInt(parts[2], 10);

                    const rowDate = new Date(year, month, day);
                    rowDate.setHours(0, 0, 0, 0); // Strip time to compare only date

                    const start = startDate ? new Date(startDate) : null;
                    const end = endDate ? new Date(endDate) : null;

                    if (start) start.setHours(0, 0, 0, 0);
                    if (end) end.setHours(23, 59, 59, 999); // Cover entire end date

                    if (start && rowDate < start) return false;
                    if (end && rowDate > end) return false;

                    return true;
                });

                // Tag filter for this column so we can remove it later
                $.fn.dataTable.ext.search[$.fn.dataTable.ext.search.length - 1].columnIndex = columnIndex;
            }

            // Redraw the table
            window.ordersDataTable.draw();
        }


        // Clear individual filter handlers
        $('.clear-filter').on('click', function () {
            const columnIndex = $(this).data('column');
            const input = $(this).siblings('.column-filter');

            input.val('');
            $(this).hide();

            // Remove active filter styling
            $('.header-filter-icon[data-column="' + columnIndex + '"]').removeClass('active-filter');

            // Clear the filter
            table.column(columnIndex).search('').draw();
            checkIfAnyFilterApplied();
        });

        // Clear date filter
        $('.clear-date-filter').on('click', function () {
            const columnIndex = $(this).data('column');

            $('.date-filter[data-column="' + columnIndex + '"]').val('');
            $(this).hide();

            // Remove active filter styling
            $('.header-filter-icon[data-column="' + columnIndex + '"]').removeClass('active-filter');

            // Clear date range filter
            $.fn.dataTable.ext.search = $.fn.dataTable.ext.search.filter(function (fn) {
                return fn.columnIndex !== columnIndex;
            });
            table.draw();
            checkIfAnyFilterApplied();
        });

        // Header filter icon click - focus on filter input
        $('.header-filter-icon').on('click', function () {
            const columnIndex = $(this).data('column');
            $('.column-filter[data-column="' + columnIndex + '"], .date-filter[data-column="' + columnIndex + '"]').first().focus();
        });

        // Toggle order details
        $ordersTable.on('click', '.order-details-toggle', function (e) {
            e.preventDefault();
            const tr = $(this).closest('tr');
            const row = table.row(tr);
            const orderId = $(this).data('id');

            if (row.child.isShown()) {
                row.child.hide();
                tr.removeClass('shown');
            } else {
                $.get(`${window.ordersDetailsUrl || '/Orders/GetOrderDetails'}/${orderId}`, function (html) {
                    row.child(html).show();
                    tr.addClass('shown');
                });
            }
        });
    }
    $('#clearAllFilters').on('click', function () {
        const table = window.ordersTable; // ✅ now it’s defined

        $('.column-filter').val('');
        $('.clear-filter').hide();

        $('.date-filter').val('');
        $('.clear-date-filter').hide();

        table.columns().search('');

        $.fn.dataTable.ext.search = [];

        table.draw();

        $('.header-filter-icon').removeClass('active-filter');
        checkIfAnyFilterApplied();
    });

    $('#receiptForm').on('submit', function (e) {
        e.preventDefault();

        const receipt = $('#receiptNumber').val().trim();
        if (!receipt) {
            toastr.warning(window.localizedTextDT?.Pleaseenterreceipt || "Please enter a receipt number.");
            return;
        }

        $.ajax({
            url: '/Payments/SubmitReceipt',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ receiptNumber: receipt }),
            success: function (res) {
                const key = res.messageKey || 'Receiptreceived';
                toastr.success(window.localizedTextDT?.Receiptverify || 'Receipt received. We will verify your payment shortly.');
                $('#receiptModal').modal('hide');
            },
            error: function () {
                toastr.error(window.localizedTextDT?.Errorreceipt || 'Error submitting receipt.');
            }
        });
    });

    $('#addApiForm').on('submit', function (e) {
        e.preventDefault();

        const formData = $(this).serialize();
        const submitButton = $('#apiCreateBtn');
        submitButton.prop('disabled', true);
        $.ajax({
            url: '/Api/Create',
            method: 'POST',
            data: formData,
            success: function (response) {
                const alertBox = $('#apiFormAlert');
                alertBox.removeClass('d-none alert-success alert-danger');

                if (response.success) {
                    toastr.success(window.localizedTextDT?.apisuccess || 'API created successfully.');
                    setTimeout(() => {
                        $('#addApiModal').modal('hide');
                        location.reload(); // optional: reload the list dynamically instead
                    }, 1000);
                } else {
                    toastr.error(response.message || window.localizedTextDT?.SomethingWentWrong || 'Something went wrong.');
                }
            },
            error: function () {
                const alertBox = $('#apiFormAlert');
                toastr.error(response.message || window.localizedTextDT?.SomethingWentWrong || 'Something went wrong.');
            },
            complete: function () {
                submitButton.prop('disabled', false);
            }
        });
    });
    $(document).on('click', '.edit-api-btn', function () {
        $('#editApiId').val($(this).data('id'));
        $('#editServiceType').val($(this).data('servicetype'));
        $('#editServiceName').val($(this).data('servicename'));
        $('#editUsername').val($(this).data('username'));
        $('#editPassword').val($(this).data('password'));
        $('#editOriginator').val($(this).data('originator'));

        $('#editApiModal').modal('show');
        bindGlobalPasswordGenerators(); // <-- Your function
    });

    // Submit Edit
    $('#editApiForm').on('submit', function (e) {
        e.preventDefault();
        const formData = $(this).serialize();

        $.post('/Api/Update', formData, function (response) {
            if (response.success) {
                $('#editApiModal').modal('hide');
                loadApis(); // reload list
            } else {
                alert(response.message);
            }
        });
    });
    function decodeHtmlEntities(str) {
        const textarea = document.createElement("textarea");
        textarea.innerHTML = str;
        return textarea.value;
    }
    // Delete
    $(document).on('click', '.delete-api-btn', function () {
        const id = $(this).data('id');
        if (confirm(decodeHtmlEntities(window.localizedTextDT?.deleteapiconfirmation || 'Are you sure you want to delete this API?'))) {
            $.post('/Api/Delete', { id }, function (response) {
                if (response.success) {
                    loadApis();
                } else {
                    alert(response.message);
                }
            });
        }
    });
    let isSubmitting = false;

    $('#addUserForm').submit(function (e) {
        e.preventDefault();
        if (isSubmitting) return; // ✅ Prevent multiple submissions
        isSubmitting = true;

        const form = $(this);
        const formData = form.serialize();
        const submitButton = form.find('button[type="submit"]');
        submitButton.prop('disabled', true);

        $.ajax({
            url: '/CompanyUsers/AddFromCompany',
            type: 'POST',
            data: formData,
            success: function (res) {
                if (res.success) {
                    $('#addUserModal').modal('hide');
                    $('#companyUsersList').DataTable().ajax.reload(null, false);
                    toastr.success(window.localizedTextDT?.companyusersuccess || 'User created successfully!');
                } else {
                    let errorMsg = "User creation failed.";
                    if (res.errors) {
                        if (Array.isArray(res.errors)) {
                            errorMsg = res.errors.join('<br>');
                        } else if (typeof res.errors === 'object') {
                            errorMsg = Object.values(res.errors).flat().join('<br>');
                        }
                    } else if (res.message) {
                        errorMsg = res.message;
                    }
                    toastr.error(errorMsg);
                }
            },
            error: function (xhr) {
                console.error(xhr);
                let errorMsg = "Unexpected error occurred while saving.";
                if (xhr.responseJSON && xhr.responseJSON.errors) {
                    const errors = xhr.responseJSON.errors;
                    if (Array.isArray(errors)) {
                        errorMsg = errors.join('<br>');
                    } else if (typeof errors === 'object') {
                        errorMsg = Object.values(errors).flat().join('<br>');
                    }
                }
                toastr.error(errorMsg);
            },
            complete: function () {
                submitButton.prop('disabled', false);
                isSubmitting = false; // ✅ Allow submission again
            }
        });
    });
    // Load APIs (AJAX)
    function loadApis() {
        $.get('/Api/List', function (html) {
            $('#apiListContainer').html(html);
        });
    }
    //  updateRoles();

});
$(document).on('mouseenter', '.dropdown-submenu', function () {
    const $submenu = $(this).find('.inner-api-list');
    const orderId = $submenu.data('order-id');

    if ($submenu.data('loaded')) return; // Already loaded

    $.get(`/Home/GetAvailableApisForOrder`, { orderId }, function (apis) {
        if (Array.isArray(apis)) {
            const html = apis.map(api =>
                `<li><a class="dropdown-item change-api-option" href="#" data-order-id="${orderId}" data-api-id="${api.apiId}">${api.serviceName}</a></li>`
            ).join('');

            $submenu.html(html);
            $submenu.data('loaded', true);
        } else {
            $submenu.html('<li><a class="dropdown-item disabled">No APIs</a></li>');
        }
    });
});

$(document).on('click', '.change-api-option', function (e) {
    e.preventDefault();
    const orderId = $(this).data('order-id');
    const newApiId = $(this).data('api-id');

    if (confirm(window.localizedTextDT.confirmchangeapi)) {
        $.post('/Home/ChangeApiForOrder', { orderId, newApiId }, function (res) {
            alert(res.message);
            $ordersTable.DataTable().ajax.reload();
        });
    }
});
$(document).ready(function () {
    $('#apiListContainer').on('click', '.set-default-api-btn', function () {
        const apiId = $(this).data('id');

        $.post('/Api/MakeDefault', { id: apiId }, function (res) {
            if (res.success) {
                toastr.success(window.localizedTextDT.defaultupdated || 'Default API updated.');

                // Reset all buttons
                $('.set-default-api-btn')
                    .removeClass('btn-success')
                    .addClass('btn-outline-secondary')
                    .text(window.localizedTextDT.makedefault || 'Make Default');

                // Highlight the clicked one
                const $thisBtn = $(`.set-default-api-btn[data-id="${apiId}"]`);
                $thisBtn
                    .removeClass('btn-outline-secondary')
                    .addClass('btn-success')
                    .text(window.localizedTextDT.Default || 'Default');
            } else {
                toastr.error(res.message || 'Failed to update default API.');
            }
        }).fail(function () {
            toastr.error('Server error.');
        });
    });
    $('#changeApiForm').on('submit', function (e) {
        e.preventDefault();

        const orderId = $('#changeOrderId').val();
        const apiId = $('#newApiId').val();

        $.post('/Home/ChangeApiAndResend', { orderId, apiId })
            .done(function (res) {
                $('#changeApiModal').modal('hide');
                toastr.success(window.localizedTextDT.apichangessmssent || 'API changed and SMS re-sent.');

                // 🔄 Refresh DataTable if using one
                if ($.fn.DataTable.isDataTable('#ordersTable')) {
                    $('#ordersTable').DataTable().ajax.reload(null, false);
                } else {
                    location.reload(); // fallback
                }
            })
            .fail(function (xhr) {
                const message = xhr.responseJSON?.message || xhr.responseText || 'Unknown error occurred';
                toastr.error('Error: ' + message);
            });
    });
    $('#addCreditForm').on('submit', function (e) {
        e.preventDefault();

        const form = $(this);
        const formData = form.serialize();
        const url = form.attr('action');

        // Disable the submit button
        const submitButton = form.find('button[type="submit"]');
        submitButton.prop('disabled', true);

        $.post(url, formData)
            .done(function (res) {
                if (res.success) {
                    toastr.success(res.message.value || 'Credit added successfully.');
                    $('#addCreditModal').modal('hide');
                    $('#addCreditForm')[0].reset();

                    // ✅ Update the credit field (formatted)
                    const formattedCredit = Number(res.newBalance).toLocaleString();
                    $('#creditValue').val(formattedCredit);



                    // Reload DataTable
                    $('#transactionList').DataTable().ajax.reload(null, false);
                } else {
                    toastr.error(res.message.value || 'Failed to add credit.');
                }
            })
            .fail(function (xhr) {
                toastr.error('Server error while adding credit.');
                console.error(xhr.responseText);
            })
            .always(function () {
                // Re-enable the button after AJAX completes
                submitButton.prop('disabled', false);
            });
    });
    $('#priceInput, #unitPrice').on('input', function () {
        const price = parseFloat($('#priceInput').val()) || 0;
        const unitPrice = parseFloat($('#unitPrice').val()) || 0;
        const credit = price > 0 && unitPrice > 0 ? Math.floor(price / unitPrice) : 0;

        $('#loanDisplay').val(credit.toLocaleString());
        $('#unitPriceHidden').val(unitPrice);
    });
    $('.unit-price-option').on('click', function () {
        const selectedPrice = $(this).data('price');
        $('#unitPrice').val(selectedPrice);
        $('#unitPriceHidden').val(selectedPrice);
    });

    //$('#manualBtn').on('click', function () {
    //    $('#unitPrice').prop('disabled', false).val('');
    //    $('#unitPriceHidden').val('');
    //});

    // Disable unit price field by default
    $('#unitPrice').prop('disabled', true);
    // Trigger calculation on load (optional)
    $('#priceInput').trigger('input');
    $('#deleteCreditForm').submit(function (e) {
        e.preventDefault();

        const form = $(this);
        const formData = form.serialize();
        const submitButton = form.find('button[type="submit"]');
        submitButton.prop('disabled', true); // 🔒 Prevent double click

        $.post('/CreditTransactions/DeleteCredit', formData)
            .done(function (res) {
                if (res.success) {
                    toastr.success(res.message.value);
                    $('#deleteCreditModal').modal('hide');
                    $('#deleteCreditForm')[0].reset();

                    if (res.newCredit !== undefined) {
                        const formattedCredit = Number(res.newCredit).toLocaleString();
                        $('#creditValue').val(formattedCredit);
                    }


                    $('#transactionList').DataTable().ajax.reload(null, false);
                } else {
                    toastr.error(res.message.value);
                }
            })
            .fail(function () {
                toastr.error('An error occurred while deleting credit.');
            })
            .always(function () {
                submitButton.prop('disabled', false); // 🔓 Re-enable after AJAX completes
            });
    });
    $('#creditTopupsTable').DataTable({
        ajax: {
            url: '/Credit/GetTopups',
            type: 'GET',
            dataSrc: function (json) {
                return json.$values || [];
            }
        },
        columns: [
            {
                data: 'companyName',
                render: function (data, type, row) {
                    return `<a href="/Companies/Details/${row.companyId}" class="text-success fw-bold">${data}</a>`;
                }
            },
            { data: 'credit' },
            {
                data: 'totalPrice',
                render: data => `₺${parseFloat(data).toLocaleString('tr-TR', { minimumFractionDigits: 2 })}`
            },
            { data: 'currency' },
            {
                data: 'unitPrice',
                render: data => `₺${parseFloat(data).toFixed(5)}`
            },
            {
                data: 'transactionDate',
                render: data => new Date(data).toLocaleString('tr-TR')
            }
        ]
    });
    $('#transactionsTable').DataTable({
        ajax: '/Transactions/GetCompanyTransactions',
        columns: [
            { data: 'transactionType' },
            { data: 'credit' },
            { data: 'totalPrice' },
            { data: 'currency' },
            { data: 'unitPrice' },
            { data: 'transactionDate' }
        ],
        responsive: true,
        order: [[5, "desc"]],
        pageLength: 10,
        language: {
            search: "",
            searchPlaceholder: "Filter...",
            emptyTable: window.localizedTextDT?.notransactionsavailable || "No Transactions Available",
            paginate: {
                previous: "<<",
                next: ">>"
            },
            info: "_START_ - _END_ / _TOTAL_ record"
        }
    });
});
function loadCompanies() {
    $.ajax({
        url: '/Companies/GetAll',
        method: 'GET',
        success: function (data) {
            const companies = data.$values;

            const $table = $('#companiesList');
            if ($.fn.DataTable.isDataTable($table)) {
                $table.DataTable().clear().destroy();
            }

            const tbody = $table.find('tbody');
            tbody.empty();

            companies.forEach(item => {
                let rowHtml = `
            <tr>
                <td style="display:none;">${item.companyId}</td>
                <td><a href="/Companies/Details/${item.companyId}">${item.companyName}</a></td>
                <td>${item.isActive ? '<i class="lni lni-checkmark text-success"></i>' : '<i class="lni lni-close text-danger"></i>'}</td>
                <td>${(item.creditLimit ?? 0).toLocaleString('en-US')}</td>
                <td>${item.apiName}</td>
                <td>${item.pricing}</td>
                <td>${item.isTrustedSender ? 'Yes' : 'No'}</td>
                <td>${item.canSendSupportRequest ? 'Yes' : 'No'}</td>
                <td>${item.isRefundable ? 'Yes' : 'No'}</td>
                <td>${item.createdAt ? new Date(item.createdAt).toLocaleString() : ''}</td>
                <td>${item.updatedAt ? new Date(item.updatedAt).toLocaleString() : ''}</td>
        `;

                if (canEditFirm) {
                    rowHtml += `
                <td class="sorting_desc">
                    <a href="javascript:void(0);" onclick="toggleCompanyActive(${item.companyId}, 'list')" title="${item.isActive ? 'Deactivate' : 'Activate'}" class="me-2">
                        <i class="lni ${item.isActive ? 'lni-pause text-warning' : 'lni-play text-success'}"></i>
                    </a>
                    <a href="javascript:void(0);" onclick="deleteCompany(${item.companyId})" title="Delete">
                        <i class="lni lni-ban text-danger"></i>
                    </a>
                </td>
            `;
                }

                rowHtml += `</tr>`;
                tbody.append(rowHtml);
            });

            const columnsConfig = [];
            if (canEditFirm) {
                columnsConfig.push({ targets: -1, width: "120px", orderable: false });
            }

            $table.DataTable({
                lengthChange: false,
                dom: 'Bfrtip',
                order: [[0, 'desc']],
                buttons: ['copy', 'excel', 'pdf', 'print'],
                columnDefs: columnsConfig
            });
        },
        error: function () {
            alert('Failed to load companies.');
        }
    });
}

function showSpinner() {
    const el = document.getElementById('globalSpinnerOverlay');
    if (el) el.style.display = 'flex';
}

function hideSpinner() {
    const el = document.getElementById('globalSpinnerOverlay');
    if (el) el.style.display = 'none';
}
function renderToggleButton(button, isActive) {
    if (!button) return;

    button.classList.remove("btn-success", "btn-warning");
    button.classList.add(isActive ? "btn-warning" : "btn-success");
    const localizedActivate = window.localizedTextDT?.activate || "Activate";
    const localizedDeactivate = window.localizedTextDT?.deactivate || "Deactivate";

    button.innerHTML = `
        <i class="lni ${isActive ? 'lni-pause' : 'lni-play'}"></i>
        ${isActive ? localizedDeactivate : localizedActivate}
    `;

    // Optional: Update internal flag if needed
    button.dataset.isActive = isActive.toString();
}

function toggleCompanyActiveNoReload(buttonEl) {
    const companyId = buttonEl.dataset.companyId;

    showSpinner();

    fetch(`/Companies/ToggleActive/${companyId}`, {
        method: 'POST'
    })
        .then(res => res.json())
        .then(result => {
            renderToggleButton(buttonEl, result.isActive);
            toastr.success(result.isActive ? window.localizedTextDT?.activate : window.localizedTextDT?.deactivate);
        })
        .catch(() => {
            toastr.error("❌ Action failed.");
        })
        .finally(() => {
            hideSpinner();
        });
}
const toggleBtn = document.getElementById('toggleUserBtn');
if (toggleBtn) {
    const isActive = toggleBtn.getAttribute('data-is-active') === 'true';
    renderToggleButton(toggleBtn, isActive);
}
function renderToggleUserButton(button, isActive) {
    const localizedActivate = window.localizedTextDT?.activate || "Activate";
    const localizedDeactivate = window.localizedTextDT?.deactivate || "Deactivate";

    button.innerHTML = isActive
        ? `<i class="lni lni-pause"></i> ${localizedDeactivate}`
        : `<i class="lni lni-play"></i> ${localizedActivate}`;

    button.dataset.isActive = isActive;
}
function toggleUserActiveNoReload(buttonEl) {
    const userId = buttonEl.dataset.userId;

    showSpinner();

    fetch(`/CompanyUsers/ToggleActiveUsers/${userId}`, {
        method: 'POST'
    })
        .then(res => res.json())
        .then(result => {
            renderToggleUserButton(buttonEl, result.isActive);
            toastr.success(result.isActive ? window.localizedTextDT?.activate : window.localizedTextDT?.deactivate);
            $('#companyUsersList').DataTable().ajax.reload(null, false);
        })
        .catch(() => {
            toastr.error("Action failed.");
        })
        .finally(() => {
            hideSpinner();
        });
}
function toggleUserActive(buttonEl) {
    const userId = buttonEl.dataset.userId;

    showSpinner();

    fetch(`/CompanyUsers/ToggleActiveUsers/${userId}`, {
        method: 'POST'
    })
        .then(res => res.json())
        .then(result => {
            renderToggleUserButton(buttonEl, result.isActive);
            toastr.success(result.isActive ? window.localizedTextDT?.activate : window.localizedTextDT?.deactivate);
        })
        .catch(() => {
            toastr.error("Action failed.");
        })
        .finally(() => {
            hideSpinner();
        });
}
document.addEventListener("DOMContentLoaded", function () {
    loadNotifications();
    setInterval(loadNotifications, 30000);
    bindGlobalPasswordGenerators();
    const modal = document.getElementById("addUserModal");

    if (modal) {
        // Bind password generator when modal is shown
        modal.addEventListener("shown.bs.modal", function () {
            bindGlobalPasswordGenerators();
        });

        // Reset form when modal is hidden
        modal.addEventListener("hidden.bs.modal", function () {
            const form = document.getElementById("addUserForm");
            if (form) form.reset();


            // Optional: clear Select2 or custom fields
            $(form).find('select').val('').trigger('change');
        });
    }

    const btn = document.getElementById('toggleCompanyBtn');
    if (btn) {
        // Use Razor-passed Model value just once on first load
        const initialIsActive = btn.getAttribute('data-is-active') === 'true';
        renderToggleButton(btn, initialIsActive);
    }
    const updateBtn = document.getElementById("updateBtn");
    const form = document.getElementById("companyDetailsForm");
    if (form && updateBtn) {
        const initialValues = {};

        // Store initial values

        form.querySelectorAll("input, select, textarea").forEach(input => {
            initialValues[input.name] = input.type === "checkbox" ? input.checked : input.value;
        });

        const checkForChanges = () => {
            const changed = [...form.elements].some(input => {
                if (!input.name) return false;
                const original = initialValues[input.name];
                const current = input.type === "checkbox" ? input.checked : input.value;
                return original != current;
            });

            updateBtn.disabled = !changed;
        };
        form.addEventListener("input", checkForChanges);
        form.addEventListener("change", checkForChanges);
        $(form).find('select').on('change.select2', checkForChanges);
    }
    let chart2;

    function loadMonthlySmsStats() {
        const el = document.querySelector("#chart2");
        if (!el) return;  // Bail out on pages without #chart2

        $.get('/Home/GetMonthlySmsStats', function (data) {
            // Prepare data arrays
            const currentMonth = data.currentMonth?.$values || [];
            const previousMonth = data.previousMonth?.$values || [];

            const days = [];
            const currentCounts = [];
            const prevCounts = [];

            for (let day = 1; day <= 31; day++) {
                days.push(day);
                const cm = currentMonth.find(x => x.day === day);
                const pm = previousMonth.find(x => x.day === day);
                currentCounts.push(cm ? cm.count : 0);
                prevCounts.push(pm ? pm.count : 0);
            }

            // Calculate total SMS and % change
            const totalCurrent = currentCounts.reduce((a, b) => a + b, 0);
            const totalPrev = prevCounts.reduce((a, b) => a + b, 0);
            const percentChange = totalPrev > 0
                ? ((totalCurrent - totalPrev) / totalPrev) * 100
                : 0;
            const percentText = percentChange.toFixed(1) + '%';

            // Update the UI
            $('#monthlySmsPercent')
                .text(percentText)
                .toggleClass('text-success', percentChange >= 0)
                .toggleClass('text-danger', percentChange < 0);

            // 2) If chart2 exists, update it; otherwise create it
            if (chart2 instanceof ApexCharts) {
                chart2.updateOptions({ xaxis: { categories: days } });
                chart2.updateSeries([
                    { name: "This Month", data: currentCounts },
                    { name: "Last Month", data: prevCounts }
                ]);
            } else {
                chart2 = new ApexCharts(el, {
                    series: [
                        { name: "This Month", data: currentCounts },
                        { name: "Last Month", data: prevCounts }
                    ],
                    chart: { height: 200, type: 'line', toolbar: { show: false } },
                    stroke: { curve: 'smooth', width: 2 },
                    xaxis: { categories: days },
                    colors: ['#00E396', '#775DD0']
                });
                chart2.render();
            }
        });
    }
    var chart7;


    const el = document.querySelector('#chart7');
    if (el) {
        chart7 = new ApexCharts(el, {
            series: [{ name: "SMS Sent", data: [] }],
            chart: { type: 'bar', height: 300 },
            plotOptions: {
                bar: { borderRadius: 8, columnWidth: '50%', endingShape: 'rounded' }
            },
            colors: ['#00E396'],
            dataLabels: { enabled: false },
            xaxis: { categories: [], labels: { style: { colors: '#fff' } } },
            yaxis: { labels: { style: { colors: '#fff' } } },
            grid: { borderColor: '#37474F' },
            tooltip: { theme: 'dark' }
        });
        chart7.render();
    }

    function loadMonthlySmsVolume() {
        if (!chart7) return;  // no chart7 container on this page

        $.get('/Home/GetMonthlySmsVolume', function (data) {
            const items = data.$values || [];
            const categories = items.map(i =>
                new Date(i.year, i.month - 1)
                    .toLocaleString('default', { month: 'short' })
            );
            const seriesData = items.map(i => i.smsCount);

            // Update chart7 only if it's initialized
            chart7.updateOptions({ xaxis: { categories: categories } });
            chart7.updateSeries([{
                name: "SMS Sent",
                data: seriesData
            }]);
        });
    }
    initChart6_ALL();
    initChart7();
    loadMonthlySmsVolume();
    loadMonthlySmsStats();
});
// Render on load

function toggleCompanyActive(companyId, source = 'details') {
    const spinner = document.getElementById('globalSpinnerOverlay');
    if (spinner) spinner.style.display = 'flex';

    fetch(`/Companies/ToggleActive/${companyId}`, { method: 'POST' })
        .then(res => res.json())
        .then(result => {
            if (result.isActive) {
                toastr.success(window.localizedTextDT.companyactivated || "Company activated.");
            } else {
                toastr.warning(window.localizedTextDT.companydeactivated || "Company deactivated.");
            }

            if (source === 'details') {
                const btn = document.getElementById('toggleCompanyBtn');
                if (btn) {
                    // Update classes
                    btn.classList.remove("btn-success", "btn-warning");
                    btn.classList.add(result.isActive ? "btn-warning" : "btn-success");

                    // Update content
                    btn.innerHTML = `
                        <i class="lni ${result.isActive ? 'lni-pause' : 'lni-play'}"></i>
                        ${result.isActive ? 'Deactivate' : 'Activate'}
                    `;
                }
            }

            if (source === 'list') {
                loadCompanies(); // Refresh table rows
            }
        })
        .catch(() => {
            toastr.error("❌ Action failed.");
        })
        .finally(() => {
            if (spinner) spinner.style.display = 'none';
        });
}
function checkIfAnyFilterApplied() {
    let anyTextFilter = $('.column-filter').filter(function () {
        return $(this).val().trim() !== '';
    }).length > 0;

    let anyDateFilter = $('.date-filter').filter(function () {
        return $(this).val().trim() !== '';
    }).length > 0;

    if (anyTextFilter || anyDateFilter) {
        $('#clearAllFilters').show();
    } else {
        $('#clearAllFilters').hide();
    }
}

$('.column-filter, .date-filter').on('click', function (e) {
    e.stopPropagation(); // prevent triggering column sort
});
$('#ordersList').on('click', '.confirm-sms-btn', function () {
    const orderId = $(this).data('id');

    if (confirm("Are you sure you want to approve and send this SMS?")) {
        $.post('/Home/ApproveOrder', { orderId }, function (res) {
            if (res.success) {
                alert(res.message.value);
                $('#ordersList').DataTable().ajax.reload(null, false);
            } else {
                alert("Error: " + res.message.value);
            }
        });
    }
});
//$('#ordersList tbody').on('click', 'td.details-control', function () {
//    const tr = $(this).closest('tr');
//    const row = $('#ordersList').DataTable().row(tr);

//    if (row.child.isShown()) {
//        // Row is open: close it
//        row.child.hide();
//        tr.find('.toggle-icon').text('➕');
//    } else {
//        // Row is closed: open it
//        const orderId = row.data().orderId;

//        $.get('/Home/GetOrderDetails', { id: orderId }, function (html) {
//            row.child(html).show();
//            tr.find('.toggle-icon').text('➖');
//        });
//    }
//});
function formatDate(value) {
    if (!value) return '';
    const date = new Date(value);
    if (isNaN(date)) return '';
    return date.toLocaleString('en-US'); // or 'tr-TR' for Turkish
}



flatpickr("#startDate, #endDate", {
    dateFormat: "Y-m-d"
});

$(document).ready(function () {
    // Toggle date filter visibility
    $('#toggleDateFilter').on('click', function () {
        $('#dateFilterContainer').slideToggle();
    });

    // Flatpickr init
    flatpickr("#startDate, #endDate", {
        dateFormat: "Y-m-d"
    });

    // Trigger DataTable redraw on change
    $('#startDate, #endDate').on('change', function () {
        $('#ordersList').DataTable().draw();
    });

    // Custom date range filter logic
    $.fn.dataTable.ext.search.push(function (settings, data, dataIndex) {
        const start = $('#startDate').val();
        const end = $('#endDate').val();
        const createdAtRaw = data[15]; // Adjust if needed based on column index

        if (!createdAtRaw) return true; // No filter if no value

        const createdDate = new Date(createdAtRaw);
        if (isNaN(createdDate)) return false;

        const startDate = start ? new Date(start) : null;
        const endDate = end ? new Date(end) : null;

        return (!startDate || createdDate >= startDate) &&
            (!endDate || createdDate <= endDate);
    });



    $('#companyUsersList').DataTable({
        responsive: true,
        ordering: false,
        ajax: {
            url: '/Companies/GetUsersByCompany/',
            type: 'GET',
            data: function (d) {
                d.companyId = $('#companyId').val();
            },
            dataSrc: function (json) {
                return json.$values || [];
            }
        },
        columns: [
            {
                data: 'userName',
                render: function (data, type, row) {
                    const star = row.isMainUser
                        ? '<i class="lni lni-user" title="Main User"></i>'
                        : '';
                    return `${data} ${star}`;
                }
            },
            {
                data: 'isActive',
                render: function (data) {
                    return data
                        ? '<i class="lni lni-checkmark-circle text-success"></i>'
                        : '<i class="lni lni-close text-danger"></i>';
                }
            },
            { data: 'fullName' },
            { data: 'quotaType' },
            { data: 'quota' },
            { data: 'email' },
            { data: 'phoneNumber' },
            { data: 'twoFA' },
            { data: 'createdAt' },
            { data: 'updatedAt' },
            canEditUsers ? {
                data: 'id', // ✅ Make sure it matches your C# return object (u.Id)
                render: function (data) {
                    return `
                    <button class="btn btn-sm btn-warning edit-user-btn" data-id="${data}">
                        <i class="lni lni-pencil-alt"></i>
                    </button>
                    <button class="btn btn-sm btn-danger delete-user-btn d-none" data-id="${data}">
                        <i class="lni lni-trash"></i>
                    </button>
                `;
                }
            } : null
        ].filter(Boolean),
        lengthChange: false,
        dom: 'Bfrtip',
        buttons: ['copy', 'excel', 'pdf', 'print']
    });
    $(document).on('click', '.delete-user-btn', function () {
        var userId = $(this).data('id');

        if (confirm(window.localizedTextDT.deleteuserconfirmation || 'Are you sure you want to delete this user?')) {
            $.ajax({
                url: '/CompanyUsers/Delete/' + userId,
                type: 'POST',
                success: function (response) {
                    if (response.success) {
                        toastr.success(response.message.value || 'User deleted successfully!');

                        // Wait a bit to let toast show before refreshing table
                        setTimeout(function () {
                            location.reload();
                        }, 200);
                    } else {
                        toastr.warning(response.message.value || 'Could not delete user.');
                    }
                },
                error: function (xhr) {
                    var message = xhr.responseJSON?.message || 'Error deleting user.';
                    toastr.error(message);
                }
            });
        }
    });

    const companyId = $('#companyId').val();
    $('#transactionList').DataTable({
        ajax: {
            url: '/CreditTransactions/GetTransactions',
            data: { companyId: companyId },
            dataSrc: function (json) {
                console.log("AJAX response:", json); // check again
                return json.data?.$values || []; // <- FIX: return correct array
            },
            error: function (xhr) {
                console.error("AJAX Error:", xhr.responseText);
            }
        },
        columns: [
            { data: 'transactionType' },
            { data: 'credit' },
            { data: 'total' },
            { data: 'currency' },
            { data: 'unitPrice' },
            { data: 'transactionDate' },
            { data: 'note' }
        ],
        order: [[5, 'desc']]
    });
    const quotaTypeSelect = $('.QuotaType');
    const quotaInput = $('.QuotaValue');

    function toggleQuotaInput() {
        if (quotaTypeSelect.val() === "Variable Quota") {
            quotaInput.prop("disabled", false);
        } else {
            quotaInput.prop("disabled", true).val(''); // Optional: clear value
        }
    }

    // Initial check on page load
    toggleQuotaInput();

    // Listen for change
    quotaTypeSelect.on("change", toggleQuotaInput);
});


$('#globalSearch,#globalSearchMobile').on('input', function () {
    const query = $(this).val();

    if (query.length > 1) {
        $('.search-content').fadeIn();

        $.get('/Home/GlobalSearch', { term: query }, function (results) {
            const data = results?.$values || []; // Extract array from $values

            const icons = {
                "Page": "menu_book",
                "Company": "business",
                "User": "person"
            };

            if (data.length === 0) {
                $('#searchResults').html('<p class="text-muted ps-3">No results found.</p>');
                return;
            }

            const html = data.map(r => `
                <div class="search-list-item d-flex align-items-center gap-3" onclick="location.href='${r.url}'" style="cursor: pointer;">
                    <div class="list-icon">
                        <i class="material-icons-outlined fs-5">${icons[r.type] || 'search'}</i>
                    </div>
                    <div>
                        <h5 class="mb-0 search-list-title">${r.name}</h5>
                        <small class="text-white">${r.type}</small>
                    </div>
                </div>
            `).join('');

            $('#searchResults').html(html);
        });
    } else {
        $('.search-content').fadeOut();
        $('#searchResults').empty();
    }
});
if (document.getElementById("updateRolesBtn")) {
    const inputs = document.querySelectorAll("input[type='checkbox'], input[type='text'], select");
    inputs.forEach(input => {
        input.addEventListener("change", () => {
            document.getElementById("updateRolesBtn").classList.remove("d-none");
        });
    });
}
let currentDownloadUrl = ''; // store globally

function openEditDirectory(id) {
    $.get(`/Directory/Get?id=${id}`, function (res) {
        if (res.success) {
            const d = res.data;

            $('#editDirectoryId').val(d.directoryId);
            $('#editDirectoryName').val(d.directoryName);
            $('#editNumbers').val('');

            $('#downloadPhonebookLink')
                .attr('data-url', `/Directory/Download?id=${d.directoryId}`)
                .html(`<i class="bi bi-download"></i> CSV'yi indirin (<span id="downloadCount">${d.count || 0}</span>)`)
                .show();

            $('#downloadCount').text(d.count || 0);

            $('#editDirectoryModal').modal('show');
        } else {
            alert(res.message);
        }
    });
}

function downloadPhonebook(anchor) {
    const url = $(anchor).attr('data-url');
    if (url) {
        window.open(url, '_blank'); // starts the download in new tab
    } else {
        alert("No download link.");
    }
}
function submitEditDirectory() {
    const formData = new FormData();
    formData.append("DirectoryId", $('#editDirectoryId').val());
    formData.append("DirectoryName", $('#editDirectoryName').val());
    formData.append("Numbers", $('#editNumbers').val());

    const file = $('#editFile')[0].files[0];
    if (file) formData.append("UploadedFile", file);

    $.ajax({
        url: '/Directory/Edit',
        type: 'POST',
        data: formData,
        processData: false,
        contentType: false,
        success: function (res) {
            if (res.success) {
                $('#editDirectoryModal').modal('hide');
                location.reload();
            } else {
                alert(res.message);
            }
        }
    });
}

//$.get('/Directory/List', function (res) {
//    if (res.success) {
//        const data = res.data;
//        const tbody = $('table tbody');
//        tbody.empty();

//        if (data.length === 0) {
//            tbody.append(`
//                <tr>
//                    <td colspan="4" class="text-center text-muted">No records found.</td>
//                </tr>
//            `);
//        } else {
//            data.forEach(d => {
//                tbody.append(`
//                    <tr>
//                        <td>${d.directoryName}</td>
//                        <td>${d.numberCount}</td>
//                        <td>${new Date(d.createdAt).toLocaleDateString()}</td>
//                        <td>
//                            <button class="btn btn-sm btn-outline-primary" onclick="openEditDirectory(${d.directoryId})">Edit</button>
//                            <button class="btn btn-sm btn-outline-danger" onclick="deleteDirectory(${d.directoryId})">Delete</button>
//                        </td>
//                    </tr>
//                `);
//            });
//        }
//    } else {
//        toastr.error(res.message || 'Failed to load directories');
//    }
//});
$('#createDirectoryBtn').on('click', function () {
    var formData = new FormData($('#directoryForm')[0]);

    $.ajax({
        url: '/Directory/Create',
        method: 'POST',
        data: formData,
        processData: false,
        contentType: false,
        success: function (res) {
            if (res.success) {
                toastr.success(window.localizedTextDT.directorysuccess || 'Directory created successfully!');
                $('#createDirectoryModal').modal('hide');
                setTimeout(() => {
                    location.reload(); // Optional: only reload after toast
                }, 1000);
            } else {
                toastr.error(window.localizedTextDT.Failed || 'An error occurred while creating the directory.');
            }
        },
        error: function () {
            toastr.error(window.localizedTextDT.Failed || 'Unexpected error occurred while saving.');
        }
    });
});


$('#editDirectorySaveBtn').on('click', function () {
    var data = {
        DirectoryId: $('#editDirectoryId').val(),
        DirectoryName: $('#editDirectoryName').val()
    };

    $.post('/Directory/Edit', data, function (res) {
        if (res.success) {
            toastr.success(res.message);
            $('#editDirectoryModal').modal('hide');
            setTimeout(() => location.reload(), 1000);
        } else {
            toastr.error(res.message);
        }
    });
});

function deleteDirectory(id) {
    if (confirm(window.localizedTextDT.deletedirectory || 'Are you sure you want to delete this directory?')) {
        $.post('/Directory/Delete', { id: id }, function (res) {
            if (res.success) {
                toastr.success(res.message);
                setTimeout(() => location.reload(), 1000);
            } else {
                toastr.error(res.message);
            }
        });
    }
}
function generatePassword() {
    const chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()";
    let password = "";
    for (let i = 0; i < 12; i++) {
        password += chars.charAt(Math.floor(Math.random() * chars.length));
    }
    document.getElementById("Password").value = password;
}

// Example submit (AJAX)
// Example submit (AJAX)
let isSubmittingform = false;

$('#createCompanyUserForm').on('submit', function (e) {
    e.preventDefault();
    if (isSubmittingform) return;

    isSubmittingform = true;

    const form = $(this);
    const submitButton = form.find('button[type="submit"]');
    submitButton.prop('disabled', true);

    $.ajax({
        url: '/CompanyUsers/Create',
        type: 'POST',
        data: form.serialize(),
        success: function (response) {
            if (response.success) {
                toastr.success(window.localizedTextDT?.companyusersuccess || 'Company user created successfully!');

                // Reload the table
                $('#companyUsersList').DataTable().ajax.reload(null, false);

                // Reset the form
                form[0].reset();

                // Close the modal (if used)
                $('#addUserModal').modal('hide');
            } else {
                let errorMessage = '❌ Error occurred.';
                if (response.errors) {
                    if (Array.isArray(response.errors)) {
                        errorMessage = response.errors.join('<br>');
                    } else if (typeof response.errors === 'object') {
                        errorMessage = Object.values(response.errors).flat().join('<br>');
                    } else {
                        errorMessage = response.errors;
                    }
                } else if (response.message) {
                    errorMessage = response.message.value || response.message;
                }
                toastr.error(errorMessage);
            }
        },
        error: function (xhr) {
            let errorMessage = '❌ Error creating user.';
            const res = xhr.responseJSON;

            if (res && res.errors) {
                if (Array.isArray(res.errors)) {
                    errorMessage = res.errors.join('<br>');
                } else if (typeof res.errors === 'object') {
                    errorMessage = Object.values(res.errors).flat().join('<br>');
                }
            } else if (res && res.message) {
                errorMessage = res.message.value || res.message;
            }

            toastr.error(errorMessage);
        },
        complete: function () {
            submitButton.prop('disabled', false);
            isSubmittingform = false;
        }
    });
});
//const { canEditUsers, isCompanyUser } = window.userPermissions;
var isCompanyUserVal = $('#isCompanyUser').val() === 'true';
$('#allcompanyUsersList').DataTable({
    ordering: true,
    order: [],
    ajax: {
        url: '/CompanyUsers/GetAllCompanyUsers',
        dataSrc: function (json) {
            return json?.$values || json; // fallback if $values doesn't exist
        }
    },
    columns: [
        {
            data: 'userName',
            render: function (data, type, row) {
                const star = row.isMainUser
                    ? '<i class="lni lni-user" title="Main User"></i>'
                    : '';
                return `<a href="/CompanyUsers/Edit/${row.id}" class="text-primary fw-bold">${data}</a> ${star}`;
            }
        },
        { data: 'isActive' },
        { data: 'fullName' },
        { data: 'createdBy' },
        {
            data: 'companyName',
            render: function (cname, type, row) {
                const id = row.companyId ?? 0;
                const isedit = $('#permission-flags-users').data('can-edit-company');

                if (isedit) {
                    return `<a href="/Companies/Details/${id}">${cname ?? '-'}</a>`;
                } else {
                    return cname ?? '-';
                }
            }
        },
        { data: 'quotaType' },
        { data: 'quota' },
        { data: 'email' },
        { data: 'phoneNumber' },
        { data: 'telegramUserId' },
        {
            data: "TwoFA",
            render: (data, type, row) => row.TwoFAEnabled ? data : "N/A"
        },
        { data: 'createdAt' },
        { data: 'updatedAt' },
        (canEditUsers) ? {
            data: 'id',
            render: function (data, type, row) {
                return `
        <a href="/CompanyUsers/Edit/${row.id}" class="btn btn-sm btn-warning"><i class="lni lni-pencil-alt"></i></a>
        <button class="btn btn-sm btn-danger delete-user-btn d-none" data-id="${row.id}"><i class="lni lni-trash"></i></button> `;
            }
        } : null
    ].filter(Boolean),
    lengthChange: false,
    dom: 'Bfrtip',
    buttons: ['copy', 'excel', 'pdf', 'print']
});


function confirmDelete(userId) {
    if (confirm(window.localizedTextDT.deleteuserconfirmation || "Are you sure you want to delete this user?")) {
        fetch(`/Users/Delete/${userId}`, {
            method: "POST"
        }).then(response => {
            if (response.ok) {
                toastr.success(window.localizedTextDT.userdeletedsuccess || "User deleted successfully");
                window.location.href = '/Users/Index';
            } else {
                toastr.error(window.localizedTextDT.Failed || "Failed to delete user.");
            }
        });
    }
}
$(document).on('submit', '#editCompanyUserForm', function (e) {
    e.preventDefault();

    const form = $(this);
    const formData = form.serialize();

    $.ajax({
        url: '/CompanyUsers/Edit', // Adjust if your controller name is different
        type: 'POST',
        data: formData,
        success: function (response) {
            toastr.success(window.localizedTextDT.companyuserupdated || "Company user updated successfully!");
            // Optional: redirect or reload
            setTimeout(() => {
                window.location.href = '/CompanyUsers/Index';
            }, 1500);
        },
        error: function (xhr) {
            toastr.error(xhr.responseText || "An error occurred while updating.");
        }
    });
});
$(document).on('submit', '#addCompanyForm', function (e) {
    e.preventDefault();
    const form = $(this);
    const submitButton = form.find('button[type="submit"]');
    submitButton.prop('disabled', true);
    const data = {
        CompanyName: $('#CompanyName').val(),
        IsTrustedSender: $('#IsTrustedSender').is(':checked'),
        IsRefundable: $('#IsRefundable').is(':checked'),
        CanSendSupportRequest: $('#CanSendSupportRequest').is(':checked'),
        Apid: parseInt($("#Apid").val()),
        CurrencyCode: $('#CurrencyCode').val(),
        LowPrice: parseFloat($('#LowPrice').val()),
        MediumPrice: parseFloat($('#MediumPrice').val()),
        HighPrice: parseFloat($('#HighPrice').val()),
        FullName: $('#FullName').val(),
        UserName: $('#UserName').val(),
        Email: $('#Email').val(),
        Phone: $('#Phone').val(),
        Password: $('#Password').val()
    };

    if (!data.CompanyName || !data.FullName || !data.Email || !data.UserName || !data.Password) {
        alert(window.localizedTextDT.fillAllRequiredFields || "Please fill all required fields.");
        submitButton.prop('disabled', false);
        return;
    }

    $.ajax({
        url: '/Companies/Add',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(data),
        success: function (response) {
            if (response.success) {
                $('#addCompanyModal').modal('hide');
                $('#addCompanyForm')[0].reset();
                toastr.success(window.localizedTextDT.Companyadded || "Company added successfully!");
                loadCompanies();
            }
        },
        error: function (xhr) {
            console.error(xhr);

            let errorMsg = window.localizedTextDT?.ErrorForm || "Error while submitting form.";

            if (xhr.responseJSON) {
                const res = xhr.responseJSON;

                if (res.success === false && res.errors) {
                    // If errors is an object with $values
                    if (res.errors.$values && Array.isArray(res.errors.$values)) {
                        errorMsg = res.errors.$values.join('<br>');
                    }
                    // Or if it's a plain array
                    else if (Array.isArray(res.errors)) {
                        errorMsg = res.errors.join('<br>');
                    }
                    // Or if it's a model state dictionary
                    else if (typeof res.errors === 'object') {
                        errorMsg = Object.values(res.errors).flat().join('<br>');
                    }
                }
            }

            toastr.error(errorMsg);
        },
        complete: function () {
            submitButton.prop('disabled', false);
        }
    });
});
$(document).on('click', '.edit-user-btn', function () {
    const userId = $(this).data('id');

    $.get(`/CompanyUsers/GetUserById/${userId}`, function (user) {
        $('#editUserId').val(user.id);
        $('#editFullName').val(user.fullName);
        $('#editUserName').val(user.userName);
        $('#editEmail').val(user.email);
        $('#editPhone').val(user.phone);
        $('#editQuotaType').val(user.quotaType || 'No quota');
        $('#editQuota').val(user.quota ?? 0);
        $('#editVerificationType').val(user.verificationType || 'none');
        const toggleBtn = $('#toggleUserBtn');
        toggleBtn.attr('data-user-id', user.id);
        toggleBtn.attr('data-is-active', user.isActive);
        renderToggleUserButton(toggleBtn[0], user.isActive);
        $('#editUserModal').modal('show');
    });
});
$('#editUserModal').on('shown.bs.modal', function () {
    bindGlobalPasswordGenerators();
});
$(document).on('click', '#addCompanyBtn', function () {
    $.get('/Companies/AddCompanyModal', function (html) {
        $('#modalContainer').html(html);
        $('#addCompanyModal').modal('show');

        $('#addCompanyModal').on('shown.bs.modal', function () {
            setTimeout(function () {
                const $apiSelect = $('.companyapi');

                if ($apiSelect.length && !$apiSelect.hasClass('select2-hidden-accessible')) {
                    $apiSelect.select2({
                        dropdownParent: $('#addCompanyModal'),
                        width: '100%'
                    });
                }
                let lastValue = '';
                document.querySelectorAll('.phoneNumberinput').forEach(function (inputEl) {
                    inputEl.addEventListener('input', function (e) {
                        const currentValue = e.target.value;
                        const isDeleting = currentValue.length < lastValue.length;

                        let input = currentValue.replace(/\D/g, ''); // remove non-digits

                        // Ensure it starts with '5'
                        if (input.length > 0 && !input.startsWith('5')) {
                            input = '5' + input.replace(/^5+/, '');
                        }

                        // Format the number
                        let formatted = input;
                        if (input.length > 3) {
                            formatted = '(' + input.slice(0, 3) + ') ' + input.slice(3);
                        }
                        if (input.length > 6) {
                            formatted = formatted.slice(0, 9) + '-' + input.slice(6);
                        }

                        if (!isDeleting || formatted.length !== currentValue.length) {
                            e.target.value = formatted;
                        }

                        lastValue = e.target.value;
                    });
                });
                $("#show_hide_password a").on('click', function (e) {
                    e.preventDefault();

                    var $input = $('#show_hide_password input');
                    var $icon = $(this).find('i'); // ✅ only target the eye icon inside the <a>

                    if ($input.attr("type") === "text") {
                        $input.attr('type', 'password');
                        $icon.removeClass("bi-eye-fill").addClass("bi-eye-slash-fill");
                    } else {
                        $input.attr('type', 'text');
                        $icon.removeClass("bi-eye-slash-fill").addClass("bi-eye-fill");
                    }
                });
                document.querySelectorAll('.password-generator').forEach(btn => {
                    btn.addEventListener('click', function () {
                        const targetId = this.dataset.passwordTarget;
                        const input = document.getElementById(targetId);
                        if (input) {
                            input.value = generateSecurePassword();
                        }
                    });
                });
            }, 500); // Adjust timeout if necessary
        });
    });
});
$('#editUserForm').submit(function (e) {
    e.preventDefault();

    const data = {
        Id: $('#editUserId').val(),
        CompanyId: $('#editCompanyId').val(),
        FullName: $('#editFullName').val(),
        UserName: $('#editUserName').val(),
        Email: $('#editEmail').val(),
        Phone: $('#editPhone').val(),
        QuotaType: $('#editQuotaType').val(),
        Quota: $('#editQuota').val(),
        VerificationType: $('#editVerificationType').val(),
        Password: $('#editPassword').val()
    };

    $.ajax({
        url: '/CompanyUsers/UpdateUser',
        method: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(data),
        success: function (res) {
            if (res.success) {
                $('#editUserModal').modal('hide');
                $('#companyUsersList').DataTable().ajax.reload(null, false);

                // ✅ Show success message
                toastr.success('User updated successfully.');
            } else {
                toastr.error('Failed to update user.');
            }
        },
        error: function () {
            toastr.error('Unexpected error occurred.');
        }
    });
});




$('#CompanyPhone').on('input', function () {
    var phone = $(this).val().replace(/\D/g, '');
    if (phone.length > 0) {
        phone = phone.replace(/^(\d{3})(\d{3})(\d{4}).*/, '$1 $2 $3');
    }
    $(this).val(phone);
});


// On Company change → update AvailableCredit
//document.getElementById('selectFirm').addEventListener('change', function () {
//    var selectedOption = this.options[this.selectedIndex];
//    var credit = selectedOption.getAttribute('data-credit') || 0;
//    document.getElementById('AvailableCredit').value = credit;
//    document.getElementById('availableCredit').innerText = parseFloat(credit).toFixed(2);

//    // Recalculate
//    updateNumbersCount();
//});

// On API change → update SmsPrice
//document.getElementById('chooseApi').addEventListener('change', function () {
//    var selectedOption = this.options[this.selectedIndex];
//    var pricing = selectedOption.getAttribute('data-pricing') || 0;
//    document.getElementById('SmsPrice').value = pricing;
//    document.getElementById('smsPrice').innerText = parseFloat(pricing).toFixed(2);

//    // Recalculate
//    updateNumbersCount();
//});

// On textarea input → update count
//document.getElementById('PhoneNumbers').addEventListener('input', updateNumbersCount);


$('#runReportBtn').on('click', function () {
    if (confirm("Are you sure you want to run the report now? 🚀")) {
        $.post('/Report/RunNow', function (res) {
            alert(res.message);
        }).fail(function () {
            alert("❌ Failed to trigger report.");
        });
    }
});
function handleStatusFilterAlternative() {
    $('#ordersList .filter-row select[data-column="2"]').on('change', function () {
        const selectedStatus = $(this).val();

        if (selectedStatus === '' || selectedStatus === null) {
            // Show all rows
            table.column(2).search('').draw();
        } else {
            // Use exact match for status
            table.column(2).search('^' + selectedStatus + '$', true, false).draw();
        }
    });
}
function debugTableData() {
    const table = $('#ordersList').DataTable();
    const data = table.rows().data();



    // Check specifically for Failed status
    const failedRows = data.toArray().filter(row => row.status === 'Failed');

    // Check what status values exist
    const statuses = data.toArray().map(row => row.status);
}




function loadTodaySmsStats() {
    $.get('/Home/GetTodaySmsStats', function (data) {
        // Update count
        $('#todaySmsCount').text(data.todayCount);
        $('#todaySmsCountGraph').text(data.todayCount);

        // Update trend arrow
        if (data.todayCount >= data.yesterdayCount) {
            $('#todaySmsTrend')
                .removeClass('ti-arrow-down-right text-danger')
                .addClass('ti-arrow-up-right text-success');
        } else {
            $('#todaySmsTrend')
                .removeClass('ti-arrow-up-right text-success')
                .addClass('ti-arrow-down-right text-danger');
        }

        // Update progress bar
        $('#todaySmsProgress')
            .css('width', data.progressPercent + '%')
            .attr('aria-valuenow', data.progressPercent);
    });
}
var chart6; // global

function initChart6_ALL() {
    const el = document.querySelector("#chart6");
    if (!el) return;        // ← nothing to do if chart container is missing

    var options = {
        series: [0, 0, 0, 0, 0],
        chart: { type: 'donut', height: 300 },
        labels: ['Delivered', 'Undelivered', 'On Hold', 'Delivery Expired', 'Refund Amount'],
        colors: ['#1E90FF', '#FF4C61', '#FFC107', '#9C27B0', '#28C76F'],
        legend: { show: false },
        dataLabels: { enabled: true },
        plotOptions: { pie: { donut: { labels: { show: false } } } }
    };

    chart6 = new ApexCharts(el, options);
    chart6.render();
}

function loadDashboardStats() {
    $.get('/Home/GetDashboardStats', function (data) {
        // Safely pull each value, defaulting to 0 if undefined
        const deliveredCount = Number(data.deliveredCount) || 0;
        const undeliverableCount = Number(data.undeliverableCount) || 0;
        const onHoldCount = Number(data.onHoldCount) || 0;
        const deliveryExpiredCount = Number(data.deliveryExpiredCount) || 0;
        const refundAmount = Number(data.refundAmount) || 0;
        const totalCount = Number(data.totalCount) || 0;

        // Donut data arrays
        const donutData_ALL = [
            deliveredCount,
            undeliverableCount,
            onHoldCount,
            deliveryExpiredCount,
            refundAmount
        ];

        const totalForDonut_ALL = donutData_ALL.reduce((a, b) => a + b, 0);

        // Update your donut chart if it exists
        if (window.chart6 && typeof window.chart6.updateSeries === 'function') {
            window.chart6.updateSeries(donutData_ALL);
        }

        // Center % text
        const percent_ALL = totalCount > 0
            ? Math.round((totalForDonut_ALL / totalCount) * 100)
            : 0;
        $('.piechart-legend h2').text(percent_ALL + '%');

        // Update the raw counts in the UI
        $('#totalCount').text(totalCount.toLocaleString());
        $('#deliveredCount').text(deliveredCount.toLocaleString());
        $('#undeliverableCount').text(undeliverableCount.toLocaleString());
        $('#onHoldCount').text(onHoldCount.toLocaleString());
        $('#deliveryExpiredCount').text(deliveryExpiredCount.toLocaleString());
        $('#refundAmount').text(refundAmount.toLocaleString());

        // Update individual segment % labels
        function pct(val) {
            return totalCount > 0
                ? Math.round((val / totalCount) * 100) + '%'
                : '0%';
        }

        $('#deliveredPercent').text(pct(deliveredCount));
        $('#undeliverablePercent').text(pct(undeliverableCount));
        $('#onHoldPercent').text(pct(onHoldCount));
        $('#deliveryExpiredPercent').text(pct(deliveryExpiredCount));
        $('#refundPercent').text(pct(refundAmount));
    });
}
var chart7;

function initChart7() {
    const el = document.querySelector("#chart7");
    if (!el) return;  // ← bail out if no container on this page

    var options = {
        series: [{ name: "SMS Sent", data: [] }],
        chart: { type: 'bar', height: 300 },
        plotOptions: {
            bar: { borderRadius: 8, columnWidth: '50%', endingShape: 'rounded' }
        },
        colors: ['#00E396'],
        dataLabels: { enabled: false },
        xaxis: {
            categories: [],
            labels: { style: { colors: '#fff' } }
        },
        yaxis: { labels: { style: { colors: '#fff' } } },
        grid: { borderColor: '#37474F' },
        tooltip: { theme: 'dark' }
    };

    chart7 = new ApexCharts(el, options);
    chart7.render();
}


function deactivateCompany(companyId) {
    if (confirm(window.localizedTextDT.deactivatecompany || "Are you sure you want to deactivate this company?")) {
        fetch(`/Companies/Deactivate/${companyId}`, {
            method: 'POST'
        })
            .then(res => {
                if (res.ok) {
                    alert(window.localizedTextDT.companydeactivatesuccess || "Company deactivated successfully.");
                    location.reload();
                } else {
                    alert(window.localizedTextDT.Failed || "Failed to deactivate company.");
                }
            });
    }
}

function deleteCompany(companyId) {
    if (confirm(window.localizedTextDT.companydeleteconfirmation || "Are you sure you want to delete this company? This cannot be undone.")) {
        fetch(`/Companies/Delete/${companyId}`, {
            method: 'DELETE'
        })
            .then(res => {
                if (res.ok) {
                    alert(window.localizedTextDT.companydeletesuccess || "Company deleted successfully.");
                    window.location.href = "/Companies";
                } else {
                    alert(window.localizedTextDT.Failed || "Failed to delete company.");
                }
            });
    }
}
$('#PhoneNumbers').on('input', debounce(function () {
    const text = $(this).val();
    const estimatedCount = text.split(/[,;\n\r]/).length;

    // Use longer debounce for large datasets
    const dynamicDelay = estimatedCount > 10000 ? 1000 : 300;

    clearTimeout(this.updateTimeout);
    this.updateTimeout = setTimeout(() => {
        updateNumbersCount();
        updateSmsPricePerUnit();
    }, dynamicDelay);
}, 300));

// ✅ Additional optimization: Clear cache when phone numbers field is cleared
$('#PhoneNumbers').on('focus', function () {
    if (!$(this).val().trim()) {
        phoneNumbersCache = {
            lastText: '',
            rawNumbers: [],
            validCount: 0,
            invalidCount: 0,
            totalCount: 0
        };
    }
});
function getUnitPriceBasedOnCount(count) {
    let price = null;

    if (count < 500000) {
        price = $('#companyLowPrice').val() || $('#globalLowPrice').val();
    } else if (count < 1000000) {
        price = $('#companyMediumPrice').val() || $('#globalMediumPrice').val();
    } else {
        price = $('#companyHighPrice').val() || $('#globalHighPrice').val();
    }

    return parseFloat(price || 0).toFixed(3);
}

function updateSmsPricePerUnit() {
    const count = updateNumbersCount();
    const unitPrice = getUnitPriceBasedOnCount(count);

    $('#smsPrice').text(unitPrice);
}

//$('#PhoneNumbers, #Message').on('blur change input', function () {
//    updateSmsPricePerUnit();
//});
function updateRoles() {
    const updateBtn = document.getElementById("updateRolesBtn");
    if (!updateBtn) {
        console.warn("❌ updateRolesBtn not found");
    }
    const inputs = document.querySelectorAll("input[type='checkbox'], input[type='text'], select");

    inputs.forEach(input => {
        input.addEventListener("change", () => {
            updateBtn.classList.remove("d-none");
        });
    });
}
function generateSecurePassword() {
    const upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    const lower = "abcdefghijklmnopqrstuvwxyz";
    const number = "0123456789";
    const special = "!@#$%^&*()_+[]{}|.<>";
    const all = upper + lower + number + special;

    let password = "";
    password += upper[Math.floor(Math.random() * upper.length)];
    password += lower[Math.floor(Math.random() * lower.length)];
    password += number[Math.floor(Math.random() * number.length)];
    password += special[Math.floor(Math.random() * special.length)];

    for (let i = 4; i < 10; i++) {
        password += all[Math.floor(Math.random() * all.length)];
    }

    return password.split('').sort(() => 0.5 - Math.random()).join('');
}

function bindGlobalPasswordGenerators() {
    document.querySelectorAll('.password-generator').forEach(btn => {
        btn.addEventListener('click', function () {
            const targetId = this.dataset.passwordTarget;
            const input = document.getElementById(targetId);
            if (input) {
                input.value = generateSecurePassword();
            }
        });
    });
}
function markAsRead(notificationId, element) {
    fetch(`/Notifications/MarkAsRead/${notificationId}`, {
        method: 'POST'
    })
        .then(response => {
            if (response.ok) {
                // Optionally: remove the notification from the DOM
                const item = element.closest('.dropdown-item');
                if (item) item.remove();

                // Optionally: update the notification count
                loadNotifications();
            } else {
                console.error('Failed to mark notification as read.');
            }
        })
        .catch(err => {
            console.error('Error marking notification as read:', err);
        });
}

function loadNotifications() {
    fetch('/Notifications/GetUnread')
        .then(r => r.json())
        .then(data => {
            const container = document.querySelector('.notify-list');
            const badge = document.getElementById('notificationCount');
            if (!container || !badge) return;

            container.innerHTML = '';

            // handle both Array and { $values: Array } shapes:
            const notifications = Array.isArray(data)
                ? data
                : (data.$values || []);

            if (notifications.length) {
                badge.innerText = notifications.length;
                badge.style.display = 'inline-block';
            } else {
                badge.innerText = '0';
                badge.style.display = 'none';
            }

            notifications.forEach(n => {
                const html = `
          <div>
            <a class="dropdown-item border-bottom py-2"
               href="javascript:;"
               onclick="markAsRead(${n.notificationId}, this)">
              <div class="d-flex align-items-center gap-3">
                <div class="user-wrapper bg-primary text-primary bg-opacity-10">
                  <span>${n.title?.substring(0, 2).toUpperCase()}</span>
                </div>
                <div>
                  <h5 class="notify-title">${n.title}</h5>
                  <p class="mb-0 notify-desc">${n.description}</p>
                  <p class="mb-0 notify-time">
                    ${new Date(n.createdAt).toLocaleString()}
                  </p>
                </div>
                <div class="notify-close position-absolute end-0 me-3">
                  <i class="material-icons-outlined fs-6">close</i>
                </div>
              </div>
            </a>
          </div>`;
                container.insertAdjacentHTML('beforeend', html);
            });
        })
        .catch(err => console.error('Failed to load notifications', err));
}