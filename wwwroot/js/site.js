(() => {
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

})();