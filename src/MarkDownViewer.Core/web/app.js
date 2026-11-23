(async function () {
  const theme = document.documentElement.dataset.theme;
  try {
    if (window.hljs)
      document.querySelectorAll('pre code').forEach(el => hljs.highlightElement(el));
    if (window.renderMathInElement)
      renderMathInElement(document.body, {
        delimiters: [
          { left: '\\[', right: '\\]', display: true },
          { left: '\\(', right: '\\)', display: false }
        ],
        throwOnError: false
      });
    if (window.mermaid && document.querySelector('.mermaid')) {
      mermaid.initialize({ startOnLoad: false, theme: theme === 'dark' ? 'dark' : 'default', securityLevel: 'strict' });
      await mermaid.run({ querySelector: '.mermaid' });
    }
  } catch (e) { console.error(e); }

  const y = parseFloat(document.body.dataset.y || '0');
  const restore = () => {
    if (y > 0) window.scrollTo(0, y);
    else if (location.hash.length > 1) {
      let id = location.hash.slice(1);
      try { id = decodeURIComponent(id); } catch (_) {}
      const el = document.getElementById(id);
      if (el) el.scrollIntoView();
    }
  };
  restore();
  const done = () => { restore(); window.__ready = true; };
  if (document.readyState === 'complete') done(); else window.addEventListener('load', done);
})();
