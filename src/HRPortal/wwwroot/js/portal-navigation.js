/* Preserve collapsible HRPortal navigation, including nested module sections. */
(() => {
  const groups = [...document.querySelectorAll('.sidebar details[data-nav-group]')];
  if (!groups.length) return;
  const normalize = value => value.replace(/\/$/, '').toLowerCase() || '/';
  const path = normalize(window.location.pathname);
  groups.forEach(group => {
    const key = 'hrportal-nav-' + group.dataset.navGroup;
    const active = [...group.querySelectorAll('a[href]')].find(a => {
      const href = normalize(new URL(a.href).pathname);
      return href === path || (href !== '/' && path.startsWith(href + '/'));
    });
    if (active) {
      group.open = true;
    } else {
      try { group.open = sessionStorage.getItem(key) === 'open'; }
      catch (_) { /* Storage is optional. */ }
    }
    group.addEventListener('toggle', () => {
      try { sessionStorage.setItem(key, group.open ? 'open' : 'closed'); }
      catch (_) { /* Navigation works without storage. */ }
    });
  });
  document.querySelectorAll('.sidebar a[href]').forEach(a => {
    const href = normalize(new URL(a.href).pathname);
    if (href === path) {
      a.classList.add('nav-active');
      a.setAttribute('aria-current', 'page');
      a.closest('details')?.setAttribute('open', '');
    }
  });
})();
