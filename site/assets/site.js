(function () {
  var query = window.matchMedia('(prefers-color-scheme: light)');
  function syncWidgets() {
    var theme = query.matches ? 'light' : 'dark';
    document.querySelectorAll('gilde-contact, gilde-support').forEach(function (el) { el.setAttribute('theme', theme); });
  }
  syncWidgets();
  query.addEventListener('change', syncWidgets);

  document.addEventListener('DOMContentLoaded', function () {
    syncWidgets();
    drawGildeMark();
    var lab = document.getElementById('lab');
    if (lab && !lab.dataset.ready) { lab.dataset.ready = 'true'; playground(lab); }
  });

  function drawGildeMark() {
    var mark = document.querySelector('.gilde-mark');
    if (!mark) return;
    if (!('IntersectionObserver' in window)) { mark.classList.add('draw'); return; }
    new IntersectionObserver(function (entries, observer) {
      entries.forEach(function (e) { if (e.isIntersecting) { mark.classList.add('draw'); observer.disconnect(); } });
    }, { threshold: .5 }).observe(mark);
  }

  var TEXT = {
    en: {
      reader: 'Reader', editor: 'Editor', cut: 'stop inheritance', cutOn: 'inheritance stopped', version: 'new version',
      mirrorWho: 'Ben, in another browser', mirrorHint: 'Updates the moment you change something. No reload.',
      mirrorEmpty: 'Ben sees nothing yet.', idle: 'Nothing happened yet. Click a folder or file name.', you: 'you',
      granted: function (p, r, n, c) { return 'granted ' + r + ' to ' + p + ' on ' + n + (c ? ', inherited by ' + c + ' items' : ''); },
      revoked: function (p, r, n) { return 'revoked ' + r + ' from ' + p + ' on ' + n; },
      stopped: function (n) { return 'stopped inheritance at ' + n; },
      resumed: function (n) { return 'resumed inheritance at ' + n; },
      bumped: function (n, a, b) { return n + ' version ' + a + ' to ' + b; }
    },
    de: {
      reader: 'Leser', editor: 'Bearbeiter', cut: 'Vererbung stoppen', cutOn: 'Vererbung gestoppt', version: 'neue Version',
      mirrorWho: 'Ben, in einem anderen Browser', mirrorHint: 'Aktualisiert sich sofort, wenn du etwas änderst. Ohne Neuladen.',
      mirrorEmpty: 'Ben sieht noch nichts.', idle: 'Noch nichts passiert. Klick auf einen Ordner- oder Dateinamen.', you: 'du',
      granted: function (p, r, n, c) { return r + ' für ' + p + ' auf ' + n + ' vergeben' + (c ? ', an ' + c + ' Elemente vererbt' : ''); },
      revoked: function (p, r, n) { return r + ' für ' + p + ' auf ' + n + ' entzogen'; },
      stopped: function (n) { return 'Vererbung bei ' + n + ' gestoppt'; },
      resumed: function (n) { return 'Vererbung bei ' + n + ' wieder aktiv'; },
      bumped: function (n, a, b) { return n + ' Version ' + a + ' auf ' + b; }
    }
  };

  function playground(lab) {
    var t = TEXT[document.documentElement.lang] || TEXT.en;
    var tree = folder('root', '/', [
      folder('mkt', 'Marketing', [
        folder('camp', 'Campaign 2026', [file('kv', 'key-visual.psd'), file('spot', 'spot-30s.mp4')]),
        folder('conf', 'Confidential', [file('budget', 'budget.xlsx')]),
        file('guide', 'brand-guide.pdf')
      ]),
      folder('eng', 'Engineering', [file('api', 'api-spec.md'), file('road', 'roadmap.md')]),
      file('hand', 'handbook.docx')
    ]);
    var byId = {};
    (function index(n, parent) { n.parent = parent; byId[n.id] = n; (n.children || []).forEach(function (c) { index(c, n); }); })(tree, null);

    var grants = { eng: [{ person: 'Ben', role: 'reader' }] };
    var cut = {};
    var person = 'Anna';
    var role = 'editor';
    var seen = visibleFor('Ben');

    lab.querySelectorAll('[data-person]').forEach(function (b) {
      b.addEventListener('click', function () { person = b.dataset.person; pressed('[data-person]', b); });
    });
    lab.querySelectorAll('[data-role]').forEach(function (b) {
      b.addEventListener('click', function () { role = b.dataset.role; pressed('[data-role]', b); });
    });
    lab.querySelectorAll('[data-role-label]').forEach(function (el) { el.textContent = t[el.dataset.roleLabel]; });

    render();

    function folder(id, name, children) { return { id: id, name: name, kind: 'folder', children: children }; }
    function file(id, name) { return { id: id, name: name, kind: 'file', version: 1 }; }

    function pressed(selector, active) {
      lab.querySelectorAll(selector).forEach(function (b) { b.setAttribute('aria-pressed', String(b === active)); });
    }

    function effective(node) {
      var inherited = node.parent && !cut[node.id] ? effective(node.parent).map(function (g) { return { person: g.person, role: g.role, own: false }; }) : [];
      var own = (grants[node.id] || []).map(function (g) { return { person: g.person, role: g.role, own: true }; });
      var merged = {};
      inherited.concat(own).forEach(function (g) {
        var current = merged[g.person];
        if (!current || g.role === 'editor' || g.own) merged[g.person] = g;
      });
      return Object.keys(merged).sort().map(function (k) { return merged[k]; });
    }

    function path(node) { return node.parent && node.parent.id !== 'root' ? path(node.parent) + '/' + node.name : '/' + (node.id === 'root' ? '' : node.name); }

    function descendants(node) {
      return (node.children || []).reduce(function (all, c) { return cut[c.id] ? all : all.concat([c], descendants(c)); }, []);
    }

    function visibleFor(who) {
      var out = {};
      Object.keys(byId).forEach(function (id) {
        if (id === 'root') return;
        var g = effective(byId[id]).filter(function (e) { return e.person === who; })[0];
        if (g) out[id] = g.role;
      });
      return out;
    }

    function toggleGrant(node) {
      var list = grants[node.id] = grants[node.id] || [];
      var existing = list.filter(function (g) { return g.person === person; })[0];
      if (existing && existing.role === role) {
        grants[node.id] = list.filter(function (g) { return g !== existing; });
        log(t.revoked(person, t[role], path(node)));
      } else {
        grants[node.id] = list.filter(function (g) { return g.person !== person; }).concat([{ person: person, role: role }]);
        log(t.granted(person, t[role], path(node), descendants(node).length));
      }
      render();
    }

    function toggleCut(node) {
      cut[node.id] = !cut[node.id];
      log(cut[node.id] ? t.stopped(path(node)) : t.resumed(path(node)));
      render();
    }

    function bump(node) {
      node.version += 1;
      node.bumped = true;
      log(t.bumped(path(node), node.version - 1, node.version));
      render();
      node.bumped = false;
    }

    function log(message) {
      var tape = lab.querySelector('.tape ol');
      var idle = tape.querySelector('.idle');
      if (idle) idle.remove();
      var li = document.createElement('li');
      var now = new Date();
      li.innerHTML = '<time></time><span class="actor"></span><span><span class="op">●</span> <span class="msg"></span></span>';
      li.querySelector('time').textContent = now.toTimeString().slice(0, 8);
      li.querySelector('.actor').textContent = t.you;
      li.querySelector('.msg').textContent = message;
      tape.prepend(li);
    }

    function render() {
      var root = lab.querySelector('.tree');
      root.innerHTML = '';
      tree.children.forEach(function (c) { root.appendChild(renderNode(c)); });
      renderMirror();
    }

    function renderNode(node) {
      var li = document.createElement('li');
      if (cut[node.id]) li.className = 'blocked';
      var row = document.createElement('div');
      row.className = 'node';

      var kind = document.createElement('span');
      kind.className = 'kind';
      kind.textContent = node.kind === 'folder' ? '▸' : '·';
      row.appendChild(kind);

      var name = document.createElement('button');
      name.className = 'name';
      name.type = 'button';
      name.textContent = node.name + (node.kind === 'folder' ? '/' : '');
      name.addEventListener('click', function () { toggleGrant(node); });
      row.appendChild(name);

      if (node.kind === 'file') {
        var ver = document.createElement('span');
        ver.className = 'ver' + (node.bumped ? ' bump' : '');
        ver.textContent = 'v' + node.version;
        row.appendChild(ver);
      }

      var badges = document.createElement('span');
      badges.className = 'badges';
      effective(node).forEach(function (g) {
        var b = document.createElement('span');
        b.className = 'badge' + (g.own ? ' own' : '');
        b.textContent = g.person + ' · ' + t[g.role];
        badges.appendChild(b);
      });
      row.appendChild(badges);

      var action = document.createElement('button');
      action.type = 'button';
      action.className = 'cut';
      if (node.kind === 'folder') {
        action.setAttribute('aria-pressed', String(!!cut[node.id]));
        action.textContent = cut[node.id] ? t.cutOn : t.cut;
        action.addEventListener('click', function () { toggleCut(node); });
      } else {
        action.textContent = '+ ' + t.version;
        action.addEventListener('click', function () { bump(node); });
      }
      row.appendChild(action);
      li.appendChild(row);

      if (node.children) {
        var ul = document.createElement('ul');
        node.children.forEach(function (c) { ul.appendChild(renderNode(c)); });
        li.appendChild(ul);
      }
      return li;
    }

    function renderMirror() {
      var now = visibleFor('Ben');
      var list = lab.querySelector('.mirror ul');
      list.innerHTML = '';
      var ids = Object.keys(now);
      if (!ids.length) {
        var empty = document.createElement('li');
        empty.className = 'empty';
        empty.textContent = t.mirrorEmpty;
        list.appendChild(empty);
      }
      ids.forEach(function (id) {
        var li = document.createElement('li');
        if (seen[id] !== now[id]) li.className = 'fresh';
        var left = document.createElement('span');
        left.textContent = path(byId[id]) + (byId[id].kind === 'file' ? '  v' + byId[id].version : '');
        var right = document.createElement('span');
        right.textContent = t[now[id]];
        li.appendChild(left);
        li.appendChild(right);
        list.appendChild(li);
      });
      var dot = lab.querySelector('.live');
      dot.classList.remove('ping');
      void dot.offsetWidth;
      dot.classList.add('ping');
      seen = now;
    }

    lab.querySelector('.mirror .who').textContent = t.mirrorWho;
    lab.querySelector('.mirror .hint').textContent = t.mirrorHint;
    lab.querySelector('.tape .idle').textContent = t.idle;
  }
})();
