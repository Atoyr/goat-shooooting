(() => {
  const body = document.body;
  const menuButton = document.querySelector('.menu-button');
  const sidebarLinks = [...document.querySelectorAll('.sidebar a')];
  const sections = [...document.querySelectorAll('main > section.searchable')];
  const search = document.querySelector('#manualSearch');
  const noResults = document.querySelector('.no-results');

  menuButton?.addEventListener('click', () => {
    const open = body.classList.toggle('menu-open');
    menuButton.setAttribute('aria-expanded', String(open));
    menuButton.setAttribute('aria-label', open ? 'メニューを閉じる' : 'メニューを開く');
  });

  sidebarLinks.forEach(link => link.addEventListener('click', () => {
    body.classList.remove('menu-open');
    menuButton?.setAttribute('aria-expanded', 'false');
  }));

  document.addEventListener('click', event => {
    if (!body.classList.contains('menu-open')) return;
    if (event.target.closest('.sidebar') || event.target.closest('.menu-button')) return;
    body.classList.remove('menu-open');
    menuButton?.setAttribute('aria-expanded', 'false');
  });

  document.querySelectorAll('pre').forEach(block => {
    const code = block.querySelector('code');
    if (!code) return;
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'copy-button';
    button.textContent = 'COPY';
    button.setAttribute('aria-label', 'コードをコピー');
    button.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(code.textContent);
        button.textContent = 'COPIED';
        button.classList.add('copied');
        window.setTimeout(() => {
          button.textContent = 'COPY';
          button.classList.remove('copied');
        }, 1600);
      } catch {
        button.textContent = 'SELECT';
        const selection = window.getSelection();
        const range = document.createRange();
        range.selectNodeContents(code);
        selection.removeAllRanges();
        selection.addRange(range);
      }
    });
    block.append(button);
  });

  const normalize = value => value.toLocaleLowerCase('ja').replaceAll(/\s+/g, ' ').trim();
  const applySearch = value => {
    const query = normalize(value);
    let visible = 0;
    sections.forEach(section => {
      const matches = !query || normalize(section.textContent).includes(query);
      section.hidden = !matches;
      if (matches) visible += 1;
    });
    noResults.hidden = visible !== 0;
  };
  search?.addEventListener('input', () => applySearch(search.value));
  document.querySelectorAll('[data-search-example]').forEach(button => button.addEventListener('click', () => {
    search.value = button.dataset.searchExample;
    applySearch(search.value);
    search.focus();
  }));

  if ('IntersectionObserver' in window) {
    const observer = new IntersectionObserver(entries => {
      const current = entries
        .filter(entry => entry.isIntersecting && !entry.target.hidden)
        .sort((left, right) => right.intersectionRatio - left.intersectionRatio)[0];
      if (!current) return;
      sidebarLinks.forEach(link => link.classList.toggle('active', link.hash === `#${current.target.id}`));
    }, { rootMargin: '-20% 0px -65%', threshold: [0, .2, .6] });
    sections.forEach(section => observer.observe(section));
  }
})();
