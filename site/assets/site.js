(function () {
  var query = window.matchMedia('(prefers-color-scheme: light)');
  function apply() {
    var theme = query.matches ? 'light' : 'dark';
    document.querySelectorAll('gilde-contact, gilde-support').forEach(function (el) { el.setAttribute('theme', theme); });
  }
  apply();
  query.addEventListener('change', apply);

  document.addEventListener('DOMContentLoaded', function () {
    apply();
    var mark = document.querySelector('.gilde-mark');
    if (!mark) return;
    if (!('IntersectionObserver' in window)) { mark.classList.add('draw'); return; }
    new IntersectionObserver(function (entries, observer) {
      entries.forEach(function (e) { if (e.isIntersecting) { mark.classList.add('draw'); observer.disconnect(); } });
    }, { threshold: .5 }).observe(mark);
  });
})();
