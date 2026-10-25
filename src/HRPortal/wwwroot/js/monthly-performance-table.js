/* Module-only client-side search and sorting; never changes server authorization. */
document.querySelectorAll('.mp-filter-table').forEach(table => {
    const container = table.closest('.mp-card') || table.parentElement;
    const input = container?.querySelector('.mp-search');
    const rows = [...table.querySelectorAll('tbody tr')];
    if (input) input.addEventListener('input', () => {
        const text = input.value.trim().toLocaleLowerCase('fa');
        rows.forEach(row => { row.hidden = !row.textContent.toLocaleLowerCase('fa').includes(text); });
    });
    table.querySelectorAll('thead th').forEach((th, column) => {
        th.tabIndex = 0;
        th.title = 'مرتب‌سازی بر اساس این ستون';
        let ascending = true;
        const sort = () => {
            rows.sort((a, b) => {
                const left = a.cells[column]?.textContent.trim() || '';
                const right = b.cells[column]?.textContent.trim() || '';
                const numberA = Number(left.replace(/[٬,%٪\s]/g,''));
                const numberB = Number(right.replace(/[٬,%٪\s]/g,''));
                const result = left && right && Number.isFinite(numberA) && Number.isFinite(numberB)
                    ? numberA - numberB
                    : left.localeCompare(right, 'fa', {numeric:true, sensitivity:'base'});
                return ascending ? result : -result;
            });
            rows.forEach(row => table.tBodies[0].appendChild(row));
            ascending = !ascending;
        };
        th.addEventListener('click',sort);
        th.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') {e.preventDefault();sort();} });
    });
});
