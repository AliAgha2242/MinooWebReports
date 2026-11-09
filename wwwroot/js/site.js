(() => {
    document.querySelectorAll("[data-report-run-form]").forEach(form => {
        form.addEventListener("submit", event => {
            if (!form.checkValidity()) return;
            const overlay = form.querySelector("[data-report-loading]");
            const button = form.querySelector("[data-report-run-button]");
            if (overlay) overlay.hidden = false;
            if (button) {
                button.disabled = true;
                button.innerHTML = '<span class="reports-button-spinner" aria-hidden="true"></span> در حال آماده‌سازی...';
            }
        });
    });

    // Collapsible searchable multi-select dropdowns.
    document.querySelectorAll("[data-searchable-listbox]").forEach(container => {
        const trigger = container.querySelector("[data-listbox-trigger]");
        const dropdown = container.querySelector("[data-listbox-dropdown]");
        const search = container.querySelector("[data-listbox-search]");
        const list = container.querySelector("[data-listbox-options]");
        const status = container.querySelector("[data-listbox-status]");
        const hiddenInputs = container.querySelector("[data-listbox-hidden-inputs]");
        const selectionLabel = container.querySelector("[data-listbox-selection-label]");
        if (!trigger || !dropdown || !search || !list || !hiddenInputs) return;

        const items = Array.from(list.querySelectorAll("[data-listbox-option]"));
        const initialLimit = 100;
        const normalize = value => String(value ?? "").trim().toLocaleLowerCase();
        const selectedItems = () => items.filter(item => item.querySelector("[data-listbox-checkbox]")?.checked);

        const closeDropdown = () => {
            dropdown.hidden = true;
            trigger.setAttribute("aria-expanded", "false");
            container.classList.remove("is-open");
        };
        const openDropdown = () => {
            dropdown.hidden = false;
            trigger.setAttribute("aria-expanded", "true");
            container.classList.add("is-open");
            renderOptions();
            search.focus();
        };
        const syncSelectedValues = () => {
            hiddenInputs.replaceChildren();
            selectedItems().forEach(item => {
                const checkbox = item.querySelector("[data-listbox-checkbox]");
                const input = document.createElement("input");
                input.type = "hidden";
                input.name = list.dataset.fieldName;
                input.value = checkbox.value;
                hiddenInputs.appendChild(input);
            });
            if (selectionLabel) {
                const selected = selectedItems();
                selectionLabel.textContent = selected.length
                    ? `${selected.length} مورد انتخاب شده`
                    : "انتخاب کنید...";
            }
        };
        const renderOptions = () => {
            const query = normalize(search.value);
            const matches = items.filter(item => normalize(item.dataset.searchText).includes(query));
            const visibleItems = query ? matches.slice(0, initialLimit) : items.slice(0, initialLimit);
            const visibleSet = new Set(visibleItems);
            items.forEach(item => {
                const checkbox = item.querySelector("[data-listbox-checkbox]");
                item.hidden = !visibleSet.has(item) && !checkbox.checked;
            });
            if (status) {
                const selectedCount = selectedItems().length;
                const shownCount = visibleItems.length + items.filter(item =>
                    item.querySelector("[data-listbox-checkbox]")?.checked && !visibleSet.has(item)).length;
                status.textContent = query
                    ? `${shownCount} مورد از ${matches.length} نتیجه نمایش داده شده · ${selectedCount} انتخاب‌شده`
                    : `${items.length > initialLimit ? "۱۰۰ گزینه اول از " + items.length : items.length} گزینه · ${selectedCount} انتخاب‌شده`;
            }
        };

        trigger.addEventListener("click", () => {
            if (dropdown.hidden) openDropdown();
            else closeDropdown();
        });
        list.addEventListener("change", event => {
            if (!event.target.matches("[data-listbox-checkbox]")) return;
            syncSelectedValues();
            renderOptions();
            closeDropdown();
        });
        search.addEventListener("input", renderOptions);
        search.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                closeDropdown();
                trigger.focus();
            }
        });
        document.addEventListener("pointerdown", event => {
            if (!container.contains(event.target)) closeDropdown();
        });
        syncSelectedValues();
        renderOptions();
        closeDropdown();
    });

    const toggle = document.querySelector("[data-password-toggle]");
    const password = document.querySelector("#Password");
    if (toggle && password) {
        toggle.addEventListener("click", () => {
            const show = password.type === "password";
            password.type = show ? "text" : "password";
            toggle.setAttribute("aria-label", show ? "مخفی کردن رمز عبور" : "نمایش رمز عبور");
            toggle.classList.toggle("is-visible", show);
        });
    }

    const profilePage = document.querySelector(".profile-page");
    const theme = localStorage.getItem("minoo-theme") || "dark";
    const icon = localStorage.getItem("minoo-profile-icon") || "م";
    document.documentElement.dataset.theme = theme;
    if (profilePage) profilePage.dataset.theme = theme;

    document.querySelectorAll("[data-user-avatar]").forEach(item => item.textContent = icon);
    document.querySelectorAll("[data-theme-value]").forEach(button => {
        button.classList.toggle("selected", button.dataset.themeValue === theme);
        button.addEventListener("click", () => {
            const value = button.dataset.themeValue;
            localStorage.setItem("minoo-theme", value);
            document.documentElement.dataset.theme = value;
            document.documentElement.style.colorScheme = value === "light" ? "light" : "dark";
            if (profilePage) profilePage.dataset.theme = value;
            document.querySelectorAll("[data-theme-value]").forEach(item => item.classList.toggle("selected", item.dataset.themeValue === value));
        });
    });
    document.documentElement.style.colorScheme = theme === "light" ? "light" : "dark";

    document.querySelectorAll("[data-software-form]").forEach(form => {
        form.addEventListener("click", event => {
            if (event.target.closest("button, a, input, select, textarea")) return;
            form.requestSubmit();
        });
        form.addEventListener("keydown", event => {
            if (event.target !== form || (event.key !== "Enter" && event.key !== " ")) return;
            event.preventDefault();
            form.requestSubmit();
        });
    });

    document.querySelectorAll("[data-icon-value]").forEach(button => {
        button.classList.toggle("selected", button.dataset.iconValue === icon);
        button.addEventListener("click", () => {
            const value = button.dataset.iconValue;
            localStorage.setItem("minoo-profile-icon", value);
            document.querySelectorAll("[data-user-avatar]").forEach(item => item.textContent = value);
            document.querySelectorAll("[data-icon-value]").forEach(item => item.classList.toggle("selected", item.dataset.iconValue === value));
        });
    });


    const favoriteAppsList = document.querySelector("[data-favorite-apps-list]");
    const favoriteAppModal = document.querySelector("[data-favorite-app-modal]");
    if (favoriteAppsList && favoriteAppModal) {
        const favoriteForm = favoriteAppModal.querySelector("[data-favorite-app-form]");
        const favoriteError = favoriteAppModal.querySelector("[data-favorite-app-error]");
        const favoriteToken = document.querySelector('[data-favorite-app-antiforgery] input[name="__RequestVerificationToken"]')?.value;
        const nameInput = favoriteForm.elements.namedItem("name");
        const urlInput = favoriteForm.elements.namedItem("url");
        const idInput = favoriteForm.elements.namedItem("id");

        const escapeFavoriteText = value => String(value ?? "").replace(/[&<>"']/g, char => ({
            "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
        }[char]));

        const openFavoriteModal = () => {
            favoriteAppModal.hidden = false;
            favoriteAppModal.setAttribute("aria-hidden", "false");
            document.body.style.overflow = "hidden";
            nameInput.focus();
        };
        const closeFavoriteModal = () => {
            favoriteAppModal.hidden = true;
            favoriteAppModal.setAttribute("aria-hidden", "true");
            document.body.style.overflow = "";
        };

        const loadFavoriteApps = async () => {
            favoriteAppsList.innerHTML = '<p class="favorite-apps-loading">در حال دریافت اپلیکیشن‌ها...</p>';
            try {
                const response = await fetch("/Profile/FavoriteApps", { headers: { "Accept": "application/json" } });
                if (!response.ok) throw new Error("دریافت فهرست اپلیکیشن‌ها ناموفق بود.");
                const apps = await response.json();
                if (!apps.length) {
                    favoriteAppsList.innerHTML = '<p class="favorite-apps-empty-profile">هنوز اپلیکیشنی ذخیره نشده است.</p>';
                    return;
                }

                favoriteAppsList.innerHTML = apps.map(app =>
                    '<article class="favorite-app-row">' +
                    '<span class="favorite-app-row-icon">↗</span>' +
                    '<span class="favorite-app-row-copy"><strong>' + escapeFavoriteText(app.name) + '</strong><small dir="ltr">' + escapeFavoriteText(app.url) + '</small></span>' +
                    '<a class="favorite-app-open" href="' + escapeFavoriteText(app.url) + '" target="_blank" rel="noopener noreferrer">باز کردن</a>' +
                    '<button type="button" class="favorite-app-edit" data-edit-favorite-app="' + app.id + '">ویرایش</button>' +
                    '<button type="button" class="favorite-app-delete" data-delete-favorite-app="' + app.id + '">حذف</button>' +
                    '</article>'
                ).join("");

                favoriteAppsList.querySelectorAll("[data-edit-favorite-app]").forEach(button => {
                    button.addEventListener("click", () => {
                        const app = apps.find(item => item.id === button.dataset.editFavoriteApp);
                        if (!app) return;
                        idInput.value = app.id;
                        nameInput.value = app.name;
                        urlInput.value = app.url;
                        favoriteAppModal.querySelector("#favorite-app-modal-title").textContent = "ویرایش اپلیکیشن";
                        favoriteForm.querySelector(".favorite-app-save").textContent = "ذخیره تغییرات";
                        favoriteError.textContent = "";
                        openFavoriteModal();
                    });
                });

                favoriteAppsList.querySelectorAll("[data-delete-favorite-app]").forEach(button => {
                    button.addEventListener("click", async () => {
                        if (!confirm("این اپلیکیشن از فهرست منتخب شما حذف شود؟")) return;
                        button.disabled = true;
                        try {
                            const response = await fetch("/Profile/DeleteFavoriteApp", {
                                method: "POST",
                                headers: { "Content-Type": "application/json", "RequestVerificationToken": favoriteToken || "" },
                                body: JSON.stringify({ id: button.dataset.deleteFavoriteApp })
                            });
                            const payload = await response.json();
                            if (!response.ok) throw new Error(payload.message || "حذف اپلیکیشن ناموفق بود.");
                            await loadFavoriteApps();
                        } catch (error) {
                            alert(error.message);
                            button.disabled = false;
                        }
                    });
                });
            } catch (error) {
                favoriteAppsList.innerHTML = '<p class="favorite-apps-empty-profile">' + escapeFavoriteText(error.message) + '</p>';
            }
        };

        document.querySelector("[data-open-favorite-modal]")?.addEventListener("click", () => {
            favoriteForm.reset();
            idInput.value = "";
            favoriteError.textContent = "";
            favoriteAppModal.querySelector("#favorite-app-modal-title").textContent = "افزودن اپلیکیشن";
            favoriteForm.querySelector(".favorite-app-save").textContent = "ذخیره اپلیکیشن";
            openFavoriteModal();
        });
        favoriteAppModal.querySelectorAll("[data-close-favorite-modal]").forEach(button =>
            button.addEventListener("click", closeFavoriteModal));
        favoriteAppModal.addEventListener("click", event => {
            if (event.target.classList.contains("favorite-app-backdrop")) closeFavoriteModal();
        });
        document.addEventListener("keydown", event => {
            if (event.key === "Escape" && !favoriteAppModal.hidden) closeFavoriteModal();
        });

        favoriteForm.addEventListener("submit", async event => {
            event.preventDefault();
            favoriteError.textContent = "";
            const saveButton = favoriteForm.querySelector(".favorite-app-save");
            saveButton.disabled = true;
            try {
                const response = await fetch("/Profile/SaveFavoriteApp", {
                    method: "POST",
                    headers: { "Content-Type": "application/json", "RequestVerificationToken": favoriteToken || "" },
                    body: JSON.stringify({
                        id: idInput.value || null,
                        name: nameInput.value,
                        url: urlInput.value
                    })
                });
                const payload = await response.json();
                if (!response.ok) throw new Error(payload.message || "ذخیره اپلیکیشن ناموفق بود.");
                closeFavoriteModal();
                await loadFavoriteApps();
            } catch (error) {
                favoriteError.textContent = error.message;
            } finally {
                saveButton.disabled = false;
            }
        });

        loadFavoriteApps();
    }

    const mappingModal = document.querySelector("[data-mapping-modal]");
    if (mappingModal) {
        const serverList = mappingModal.querySelector("[data-server-list]");
        const serverCount = mappingModal.querySelector("[data-server-count]");
        const serverSearch = mappingModal.querySelector("[data-server-search]");
        const selectionSummary = mappingModal.querySelector("[data-selection-summary]");
        const resultsPanel = mappingModal.querySelector("[data-mapping-results]");
        const runButton = mappingModal.querySelector("[data-run-mapping]");
        const antiForgeryToken = mappingModal.querySelector('input[name="__RequestVerificationToken"]')?.value;
        let mappingServers = [];
        let isSubmittingMapping = false;

        const escapeHtml = value => String(value ?? "").replace(/[&<>"']/g, char => ({
            "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
        }[char]));

        const formatRemaining = seconds => {
            const minutes = Math.max(1, Math.ceil(Number(seconds || 0) / 60));
            const hours = Math.floor(minutes / 60);
            const rest = minutes % 60;
            return hours ? `${hours} ساعت و ${rest} دقیقه` : `${minutes} دقیقه`;
        };

        const selectedIds = () => Array.from(serverList.querySelectorAll('input[type="checkbox"]:checked'))
            .map(input => Number(input.value));

        const updateSelection = () => {
            const count = selectedIds().length;
            selectionSummary.textContent = count
                ? `${count} سرور برای اجرای نگاشت انتخاب شده است.`
                : "هنوز سروری انتخاب نشده است.";
            runButton.disabled = count === 0 || isSubmittingMapping;
        };

        const renderServers = servers => {
            mappingServers = servers || [];
            serverCount.textContent = `${mappingServers.length} سرور`;
            if (!mappingServers.length) {
                serverList.innerHTML = '<p class="mapping-empty">هیچ سروری در تنظیمات پیدا نشد.</p>';
                return;
            }

            serverList.innerHTML = mappingServers.map(server => {
                const limited = !server.canExecute;
                const state = limited
                    ? `محدود تا ${formatRemaining(server.remainingSeconds)}`
                    : "آماده اجرا";
                return `
                    <label class="mapping-server-option ${limited ? "is-limited" : ""}" data-server-searchable data-search-text="${escapeHtml(`${server.serverName} ${server.databaseName} ${server.serverId}`.toLowerCase())}">
                        <input type="checkbox" value="${Number(server.serverId)}" ${limited ? "disabled" : ""}>
                        <span class="mapping-server-copy">
                            <strong>${escapeHtml(server.serverName)}</strong>
                            <small>${escapeHtml(server.databaseName)}</small>
                        </span>
                        <span class="mapping-server-state">${state}</span>
                    </label>`;
            }).join("");

            serverList.querySelectorAll('input[type="checkbox"]').forEach(input =>
                input.addEventListener("change", updateSelection));
            updateSelection();
            filterServers();
        };


        const filterServers = () => {
            const query = (serverSearch?.value || "").trim().toLocaleLowerCase();
            const rows = Array.from(serverList.querySelectorAll("[data-server-searchable]"));
            let visibleCount = 0;
            rows.forEach(row => {
                const matches = !query || row.dataset.searchText.includes(query);
                row.hidden = !matches;
                if (matches) visibleCount++;
            });
            let emptyMessage = serverList.querySelector("[data-server-no-match]");
            if (query && visibleCount === 0 && rows.length) {
                if (!emptyMessage) {
                    emptyMessage = document.createElement("p");
                    emptyMessage.className = "mapping-empty";
                    emptyMessage.dataset.serverNoMatch = "";
                    emptyMessage.textContent = "سروری با این عبارت پیدا نشد.";
                    serverList.appendChild(emptyMessage);
                }
            } else {
                emptyMessage?.remove();
            }
        };
        serverSearch?.addEventListener("input", filterServers);

        const loadServers = async () => {
            serverList.innerHTML = '<p class="mapping-empty">در حال دریافت فهرست سرورها...</p>';
            serverCount.textContent = "در حال دریافت...";
            runButton.disabled = true;
            try {
                const response = await fetch("/api/mapping/servers", { headers: { "Accept": "application/json" } });
                if (!response.ok) throw new Error("دریافت فهرست سرورها ناموفق بود.");
                renderServers(await response.json());
            } catch (error) {
                serverList.innerHTML = `<p class="mapping-empty">${escapeHtml(error.message)}</p>`;
                serverCount.textContent = "خطا";
            }
        };

        const openMapping = async () => {
            mappingModal.hidden = false;
            mappingModal.setAttribute("aria-hidden", "false");
            document.body.style.overflow = "hidden";
            resultsPanel.innerHTML = "";
            if (serverSearch) serverSearch.value = "";
            await loadServers();
            mappingModal.querySelector("[data-close-mapping]")?.focus();
        };

        const closeMapping = () => {
            if (isSubmittingMapping) return;
            mappingModal.hidden = true;
            mappingModal.setAttribute("aria-hidden", "true");
            document.body.style.overflow = "";
        };

        document.querySelectorAll("[data-open-mapping]").forEach(item => {
            item.addEventListener("click", openMapping);
            item.addEventListener("keydown", event => {
                if (event.target !== item || (event.key !== "Enter" && event.key !== " ")) return;
                event.preventDefault();
                openMapping();
            });
        });
        mappingModal.querySelectorAll("[data-close-mapping]").forEach(button =>
            button.addEventListener("click", closeMapping));
        document.addEventListener("keydown", event => {
            if (event.key === "Escape" && !mappingModal.hidden) closeMapping();
        });

        runButton.addEventListener("click", async () => {
            const ids = selectedIds();
            if (!ids.length || isSubmittingMapping) return;

            isSubmittingMapping = true;
            runButton.disabled = true;
            runButton.querySelector("span").textContent = "در حال ارسال درخواست...";
            resultsPanel.innerHTML = '<p class="mapping-empty">درخواست‌ها در حال اجرا هستند؛ لطفاً پنجره را نبندید.</p>';

            try {
                const response = await fetch("/api/mapping/execute", {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json",
                        "RequestVerificationToken": antiForgeryToken || ""
                    },
                    body: JSON.stringify({ serverIds: ids })
                });
                const payload = await response.json();
                if (!response.ok) throw new Error(payload.message || "اجرای نگاشت ناموفق بود.");

                resultsPanel.innerHTML = (payload || []).map(item => {
                    const statusClass = item.status === "Success" ? "is-success"
                        : item.status === "Limited" ? "is-warning" : "is-error";
                    return `
                        <div class="mapping-result-item ${statusClass}">
                            <strong>${escapeHtml(item.serverName)}</strong>
                            <span>${escapeHtml(item.message)}</span>
                        </div>`;
                }).join("");
                await loadServers();
            } catch (error) {
                resultsPanel.innerHTML = `<div class="mapping-result-item is-error"><strong>خطا</strong><span>${escapeHtml(error.message)}</span></div>`;
            } finally {
                isSubmittingMapping = false;
                runButton.querySelector("span").textContent = "اجرای نگاشت سرورهای انتخاب‌شده";
                updateSelection();
            }
        });
    }



    // Per-user report grid preferences, drag-and-drop grouping and column resizing.
    const initializeReportGrid = () => {
    const reportGrid = document.querySelector("[data-report-grid]");
    if (reportGrid) {
        const reportForm = reportGrid.querySelector("[data-report-grid-form]");
        const table = reportGrid.querySelector("[data-report-table]");
        const dropzone = reportGrid.querySelector("[data-group-dropzone]");
        const palette = reportGrid.querySelector("[data-group-palette]");
        const themePicker = reportGrid.querySelector("[data-grid-theme-picker]");
        const widthsInput = reportGrid.querySelector("[data-column-widths]");
        const themeInput = reportGrid.querySelector("[data-grid-theme-input]");
        const reportId = reportGrid.dataset.reportId || "unknown";
        const userKey = reportGrid.dataset.gridUser || "browser";
        const storageKey = "minoo-report-grid:" + userKey + ":" + reportId;
        const allowedThemes = ["midnight", "light", "ocean", "contrast"];
        let browserPreferences = {};

        try {
            browserPreferences = JSON.parse(localStorage.getItem(storageKey) || "{}") || {};
        } catch {
            browserPreferences = {};
        }

        let serverColumnWidths = {};
        try {
            serverColumnWidths = JSON.parse(widthsInput?.value || "{}") || {};
        } catch {
            serverColumnWidths = {};
        }
        const columnWidths = { ...serverColumnWidths, ...(browserPreferences.columnWidths || {}) };
        const validWidth = value => Math.max(90, Math.min(800, Math.round(Number(value) || 160)));

        const applyWidths = () => {
            table?.querySelectorAll("[data-column-header]").forEach(header => {
                const column = header.dataset.columnHeader;
                if (columnWidths[column]) {
                    const width = validWidth(columnWidths[column]);
                    header.style.width = width + "px";
                    header.style.minWidth = "90px";
                    columnWidths[column] = width;
                }
            });
        };

        const applyTheme = value => {
            const theme = allowedThemes.includes(value) ? value : "midnight";
            reportGrid.dataset.gridTheme = theme;
            if (themePicker) themePicker.value = theme;
            if (themeInput) themeInput.value = theme;
            browserPreferences.theme = theme;
            return theme;
        };

        const currentGroupValues = () => Array.from(dropzone?.querySelectorAll('input[name="groupColumns"]') || [])
            .map(input => input.value);

        const renderGroups = values => {
            if (!dropzone) return;
            const selected = [...new Set(values)].slice(0, 3);
            dropzone.innerHTML = "";

            if (!selected.length) {
                const placeholder = document.createElement("span");
                placeholder.className = "reports-group-placeholder";
                placeholder.textContent = "ستون‌ها را از فهرست زیر به این قسمت بکشید";
                dropzone.appendChild(placeholder);
            }

            selected.forEach(column => {
                const chip = document.createElement("span");
                chip.className = "reports-group-chip";
                chip.draggable = true;
                chip.dataset.groupChip = "";
                chip.dataset.column = column;

                const handle = document.createElement("span");
                handle.className = "reports-drag-handle";
                handle.textContent = "⠿";
                const label = document.createElement("b");
                const source = Array.from(palette?.querySelectorAll("[data-group-source]") || [])
                    .find(item => item.dataset.column === column);
                label.textContent = source?.dataset.columnLabel || column;
                const remove = document.createElement("button");
                remove.type = "button";
                remove.dataset.removeGroup = "";
                remove.setAttribute("aria-label", "حذف گروه " + column);
                remove.textContent = "×";
                const hidden = document.createElement("input");
                hidden.type = "hidden";
                hidden.name = "groupColumns";
                hidden.value = column;

                chip.append(handle, label, remove, hidden);
                dropzone.appendChild(chip);
            });

            palette?.querySelectorAll("[data-group-source]").forEach(button => {
                button.hidden = selected.includes(button.dataset.column);
            });
        };

        const readFormPreferences = () => {
            const filters = {};
            reportForm.querySelectorAll('input[name^="filters["]').forEach(input => {
                const match = input.name.match(/^filters\[(.*)\]$/);
                if (match) filters[match[1]] = input.value;
            });
            const hiddenColumns = Array.from(reportForm.querySelectorAll('input[name="hiddenColumns"]:checked'))
                .map(input => input.value);
            const sortInput = reportForm.querySelector('[data-sort-column-input]');
            const descendingInput = reportForm.querySelector('[data-sort-direction-input]');
            const pageSizeInput = reportForm.querySelector('select[name="pageSize"]');
            return {
                theme: applyTheme(themePicker?.value || reportGrid.dataset.gridTheme),
                columnWidths: { ...columnWidths },
                filters,
                hiddenColumns,
                groupColumns: currentGroupValues(),
                sortColumn: sortInput?.value || "",
                sortDescending: descendingInput?.value === "true",
                pageSize: pageSizeInput?.value || "25"
            };
        };

        const persistPreferences = () => {
            if (!reportForm) return;
            browserPreferences = readFormPreferences();
            if (widthsInput) widthsInput.value = JSON.stringify(columnWidths);
            if (themeInput) themeInput.value = browserPreferences.theme;
            try {
                localStorage.setItem(storageKey, JSON.stringify(browserPreferences));
            } catch {
                // Redis persistence still runs when the form is submitted.
            }
        };

        // Restore browser-local choices over the server defaults, if this browser has them.
        applyTheme(browserPreferences.theme || reportGrid.dataset.gridTheme);
        applyWidths();

        if (browserPreferences.filters) {
            reportForm.querySelectorAll('input[name^="filters["]').forEach(input => {
                const match = input.name.match(/^filters\[(.*)\]$/);
                if (match && Object.prototype.hasOwnProperty.call(browserPreferences.filters, match[1]))
                    input.value = browserPreferences.filters[match[1]];
            });
        }
        if (Array.isArray(browserPreferences.hiddenColumns)) {
            reportForm.querySelectorAll('input[name="hiddenColumns"]').forEach(input => {
                input.checked = browserPreferences.hiddenColumns.includes(input.value);
            });
        }
        if (browserPreferences.sortColumn !== undefined) {
            const sortInput = reportForm.querySelector('[data-sort-column-input]');
            if (sortInput) sortInput.value = browserPreferences.sortColumn;
        }
        if (browserPreferences.sortDescending !== undefined) {
            const descendingInput = reportForm.querySelector('[data-sort-direction-input]');
            if (descendingInput) descendingInput.value = browserPreferences.sortDescending ? "true" : "false";
        }
        const updateSortButtons = () => {
            const selectedColumn = reportForm.querySelector('[data-sort-column-input]')?.value || "";
            const descending = reportForm.querySelector('[data-sort-direction-input]')?.value === "true";
            table?.querySelectorAll('[data-sort-direction]').forEach(button => {
                const active = button.dataset.sortColumn === selectedColumn &&
                    button.dataset.sortDirection === (descending ? "desc" : "asc");
                button.classList.toggle("is-active", active);
                button.setAttribute("aria-pressed", active ? "true" : "false");
            });
        };
        updateSortButtons();
        if (browserPreferences.pageSize) {
            const pageSizeInput = reportForm.querySelector('select[name="pageSize"]');
            if (pageSizeInput) pageSizeInput.value = browserPreferences.pageSize;
        }
        if (Array.isArray(browserPreferences.groupColumns))
            renderGroups(browserPreferences.groupColumns);
        else
            renderGroups(currentGroupValues());

        themePicker?.addEventListener("change", () => {
            applyTheme(themePicker.value);
            persistPreferences();
        });

        // Resize from the left edge (RTL layout), with a hard 90px minimum.
        table?.querySelectorAll("[data-column-resizer]").forEach(handle => {
            handle.addEventListener("pointerdown", event => {
                event.preventDefault();
                const header = handle.closest("[data-column-header]");
                if (!header) return;
                const column = header.dataset.columnHeader;
                const startX = event.clientX;
                const startWidth = header.getBoundingClientRect().width;

                const move = moveEvent => {
                    const width = validWidth(startWidth + (startX - moveEvent.clientX));
                    columnWidths[column] = width;
                    header.style.width = width + "px";
                    header.style.minWidth = "90px";
                };
                const finish = () => {
                    document.removeEventListener("pointermove", move);
                    document.removeEventListener("pointerup", finish);
                    document.removeEventListener("pointercancel", finish);
                    persistPreferences();
                };
                document.addEventListener("pointermove", move);
                document.addEventListener("pointerup", finish, { once: true });
                document.addEventListener("pointercancel", finish, { once: true });
            });
        });

        // Drag a column into the grouping area, then reorder grouped columns by dragging chips.
        let draggedGroup = null;
        const startGroupDrag = (event, element, fromPalette) => {
            const column = element?.dataset.column;
            if (!column) return;
            draggedGroup = { column, fromPalette };
            element.classList.add("is-dragging");
            event.dataTransfer?.setData("text/plain", JSON.stringify(draggedGroup));
            if (event.dataTransfer) event.dataTransfer.effectAllowed = "move";
        };
        palette?.addEventListener("dragstart", event => {
            const source = event.target.closest("[data-group-source]");
            if (source) startGroupDrag(event, source, true);
        });
        dropzone?.addEventListener("dragstart", event => {
            const chip = event.target.closest("[data-group-chip]");
            if (chip) startGroupDrag(event, chip, false);
        });
        document.addEventListener("dragend", () => {
            reportGrid.querySelectorAll(".is-dragging").forEach(element => element.classList.remove("is-dragging"));
            dropzone?.classList.remove("is-drag-over");
            draggedGroup = null;
        });
        dropzone?.addEventListener("dragover", event => {
            if (!draggedGroup) return;
            event.preventDefault();
            dropzone.classList.add("is-drag-over");
        });
        dropzone?.addEventListener("dragleave", event => {
            if (!dropzone.contains(event.relatedTarget)) dropzone.classList.remove("is-drag-over");
        });
        dropzone?.addEventListener("drop", event => {
            if (!draggedGroup) return;
            event.preventDefault();
            dropzone.classList.remove("is-drag-over");
            const values = currentGroupValues();
            const targetChip = event.target.closest("[data-group-chip]");
            const targetColumn = targetChip?.dataset.column;
            const sourceColumn = draggedGroup.column;

            if (draggedGroup.fromPalette) {
                if (values.includes(sourceColumn) || values.length >= 3) return;
                const targetIndex = targetColumn ? values.indexOf(targetColumn) : -1;
                if (targetIndex >= 0) values.splice(targetIndex, 0, sourceColumn);
                else values.push(sourceColumn);
            } else {
                const sourceIndex = values.indexOf(sourceColumn);
                if (sourceIndex < 0) return;
                values.splice(sourceIndex, 1);
                const targetIndex = targetColumn ? values.indexOf(targetColumn) : -1;
                if (targetIndex >= 0) values.splice(targetIndex, 0, sourceColumn);
                else values.push(sourceColumn);
            }
            renderGroups(values);
            persistPreferences();
        });
        dropzone?.addEventListener("click", event => {
            const remove = event.target.closest("[data-remove-group]");
            if (!remove) return;
            const column = remove.closest("[data-group-chip]")?.dataset.column;
            renderGroups(currentGroupValues().filter(value => value !== column));
            persistPreferences();
        });

        table?.addEventListener("click", event => {
            const sortButton = event.target.closest("[data-sort-direction]");
            if (!sortButton) return;
            const sortInput = reportForm.querySelector('[data-sort-column-input]');
            const directionInput = reportForm.querySelector('[data-sort-direction-input]');
            if (!sortInput || !directionInput) return;
            sortInput.value = sortButton.dataset.sortColumn || "";
            directionInput.value = sortButton.dataset.sortDirection === "desc" ? "true" : "false";
            const pageInput = reportForm.querySelector('input[name="page"]');
            if (pageInput) pageInput.remove();
            updateSortButtons();
            persistPreferences();
            reportForm.requestSubmit();
        });

        reportForm?.addEventListener("submit", persistPreferences);
        reportForm?.querySelectorAll('input[name^="filters["], input[name="hiddenColumns"], select[name="pageSize"]')
            .forEach(input => input.addEventListener("change", persistPreferences));

        // Ensure the first submission also sends the initial Redis/browser state.
        if (widthsInput) widthsInput.value = JSON.stringify(columnWidths);
        if (themeInput) themeInput.value = reportGrid.dataset.gridTheme;
    };

    initializeReportGrid();
    document.addEventListener("report-grid-updated", initializeReportGrid);

})();