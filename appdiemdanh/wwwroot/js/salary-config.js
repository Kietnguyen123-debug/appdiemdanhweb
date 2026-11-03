(() => {
    const closeAllSalaryModals = () => {
        document.querySelectorAll('.salary-modal').forEach(modal => {
            modal.hidden = true;
            modal.style.display = 'none';
            modal.style.pointerEvents = 'none';
            modal.style.zIndex = '';
        });
    };

    const showSalaryModal = modal => {
        if (!modal) return;
        modal.hidden = false;
        modal.style.display = 'flex';
        modal.style.pointerEvents = 'auto';
        modal.style.zIndex = '3000';
    };

    window.openSalaryModal = (modalId, sourceButton) => {
        closeAllSalaryModals();
        const modal = document.getElementById(modalId);
        if (!modal) return false;
        if (modalId === 'salary-template-details-modal' && sourceButton) {
            modal.querySelector('[data-template-details-title]').textContent = `Cấu hình mẫu: ${sourceButton.dataset.templateName}`;
            modal.querySelector('[data-template-details-group]').textContent = sourceButton.dataset.templateGroup || '';
            modal.querySelector('[data-template-details-components]').textContent = sourceButton.dataset.templateComponents || '';
        }
        showSalaryModal(modal);
        return false;
    };

    window.openSalaryDeleteModal = button => {
        const modal = document.getElementById('salary-delete-modal');
        const form = modal?.querySelector('[data-delete-form]');
        if (!modal || !form) return false;
        const kind = button.dataset.openDeleteModal === 'component' ? 'DeleteComponent' : 'DeleteTemplate';
        form.action = `${window.location.pathname}?handler=${kind}`;
        form.querySelector('[data-delete-id]').value = button.dataset.deleteId || '';
        form.querySelector('[data-delete-subtab]').value = button.dataset.openDeleteModal === 'component' ? 'components' : 'templates';
        form.requestSubmit();
        return false;
    };

    const closest = (target, selector) => target instanceof Element ? target.closest(selector) : null;
    const salaryModule = () => document.getElementById('salary-module');
    const employeeDrawer = target => closest(target, '#employee-drawer');

    const updateState = (tab, subtab) => {
        const root = salaryModule();
        if (!root) return;
        const tabInput = root.querySelector('[data-salary-active-tab]');
        const subtabInput = root.querySelector('[data-salary-active-subtab]');
        if (tabInput) tabInput.value = tab;
        if (subtabInput) subtabInput.value = subtab || '';
        document.querySelectorAll('[data-open-salary-modal]')
            .forEach(button => {
                const form = document.querySelector(`[data-salary-modal="${button.dataset.openSalaryModal}"] form`);
                const tabField = form?.querySelector('input[name="ActiveTab"]');
                const subtabField = form?.querySelector('input[name="ActiveSubtab"]');
                if (tabField) tabField.value = tab;
                if (subtabField) subtabField.value = subtab || '';
            });
    };

    document.addEventListener('click', event => {
        const clickedElement = event.target instanceof Element ? event.target : null;
        if (!clickedElement) return;

        const openModal = closest(clickedElement, '[data-open-salary-modal]');
        if (openModal) {
            event.preventDefault();
            closeAllSalaryModals();
            const modal = document.querySelector(`[data-salary-modal="${openModal.dataset.openSalaryModal}"]`);
            if (modal) {
                const title = modal.querySelector('[data-template-details-title]');
                const group = modal.querySelector('[data-template-details-group]');
                const components = modal.querySelector('[data-template-details-components]');
                if (title && openModal.dataset.templateName) title.textContent = `Cấu hình mẫu: ${openModal.dataset.templateName}`;
                if (group) group.textContent = openModal.dataset.templateGroup || '';
                if (components) components.textContent = openModal.dataset.templateComponents || '';
                showSalaryModal(modal);
            }
            return;
        }

        const deleteButton = closest(clickedElement, '[data-open-delete-modal]');
        if (deleteButton) {
            event.preventDefault();
            window.openSalaryDeleteModal(deleteButton);
            return;
        }

        const closeModal = closest(clickedElement, '[data-close-salary-modal]');
        if (closeModal) {
            event.preventDefault();
            const modal = closeModal.closest('[data-salary-modal]');
            if (modal) {
                modal.hidden = true;
                modal.style.display = 'none';
                modal.style.pointerEvents = 'none';
                modal.style.zIndex = '';
            }
            return;
        }

        const tab = closest(clickedElement, '[data-tab]');
        if (tab) {
            event.preventDefault();
            const value = tab.dataset.tab;
            const root = salaryModule();
            if (!root) return;
            root.querySelectorAll('[data-tab]').forEach(element => element.classList.toggle('is-active', element === tab));
            root.querySelectorAll('[data-panel]').forEach(element => element.classList.toggle('is-active', element.dataset.panel === value));
            updateState(value, value === 'global' ? 'components' : 'employee-list');
            return;
        }

        const subtab = closest(clickedElement, '[data-subtab]');
        if (subtab) {
            event.preventDefault();
            const group = subtab.closest('.salary-tab-panel');
            if (!group) return;
            group.querySelectorAll('[data-subtab]').forEach(element => element.classList.toggle('is-active', element === subtab));
            group.querySelectorAll('[data-subpanel]').forEach(element => element.classList.toggle('is-active', element.dataset.subpanel === subtab.dataset.subtab));
            updateState(salaryModule()?.querySelector('[data-tab].is-active')?.dataset.tab || 'global', subtab.dataset.subtab);
            return;
        }

        const drawerTab = closest(clickedElement, '[data-drawer-tab]');
        if (drawerTab) {
            event.preventDefault();
            const drawer = employeeDrawer(drawerTab);
            if (!drawer) return;
            drawer.querySelectorAll('[data-drawer-tab]').forEach(element => element.classList.toggle('is-active', element === drawerTab));
            drawer.querySelectorAll('[data-drawer-panel]').forEach(element => element.classList.toggle('is-active', element.dataset.drawerPanel === drawerTab.dataset.drawerTab));
            return;
        }

        if (closest(clickedElement, '[data-close-drawer]')) {
            event.preventDefault();
            document.getElementById('employee-drawer')?.remove();
        }
    });

    document.addEventListener('input', event => {
        const inputElement = event.target instanceof Element ? event.target : null;
        if (!inputElement) return;
        const search = closest(inputElement, '[data-employee-search]');
        const root = salaryModule();
        if (!search || !root) return;
        const query = search.value.toLowerCase();
        root.querySelectorAll('[data-employee-row]').forEach(row => {
            row.hidden = !row.dataset.search.toLowerCase().includes(query);
        });
    });
})();
