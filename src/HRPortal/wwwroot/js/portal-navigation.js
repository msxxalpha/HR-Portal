(() => {
  const groups = [...document.querySelectorAll('.sidebar .nav-group[data-nav-group]')];
  if (!groups.length) return;
  const path = window.location.pathname.toLowerCase();
  groups.forEach(group => {
    const name = group.dataset.navGroup;
    const links = [...group.querySelectorAll('a[href]')];
    const activeLink = links.find(a => {
      const href = new URL(a.href).pathname.toLowerCase();
      return path === href || (href !== '/' && path.startsWith(href + '/'));
    });
    if (activeLink) {
      group.open = true;
      activeLink.classList.add('nav-active');
      activeLink.setAttribute('aria-current', 'page');
    } else {
      try {
        group.open = sessionStorage.getItem('hrportal-nav-' + name) === 'open';
      } catch (_) { /* storage is optional */ }
    }
    group.addEventListener('toggle', () => {
      try { sessionStorage.setItem('hrportal-nav-' + name, group.open ? 'open' : 'closed'); }
      catch (_) { /* blocked storage should not affect navigation */ }
    });
  });
})();
