// Link śledzący (hantly.pl/go/kod → ?src=kod): przenosimy kod w adresach linków i formularzy na stronie Hantly,
// żeby kreator wiedział, skąd przyszedł trener. Bez cookie i bez pamięci przeglądarki — kod żyje tylko w adresie.
(function () {
    var skip = /^\/(panel|login|logout|api|go|media|_framework|_content|subskrypcja|rachunek)(\/|$|\?)/;
    function code() {
        var m = /[?&]src=([a-z0-9]{4,12})(&|$)/.exec(location.search);
        return m ? m[1] : null;
    }
    function withSrc(href, c) {
        if (!href || href.charAt(0) !== '/' || href.charAt(1) === '/' || skip.test(href) || /[?&]src=/.test(href)) return null;
        var i = href.indexOf('#'), path = i >= 0 ? href.slice(0, i) : href, hash = i >= 0 ? href.slice(i) : '';
        return path + (path.indexOf('?') >= 0 ? '&' : '?') + 'src=' + c + hash;
    }
    function apply() {
        var c = code();
        if (!c) return;
        document.querySelectorAll('a[href^="/"]').forEach(function (a) {
            var h = withSrc(a.getAttribute('href'), c);
            if (h) a.setAttribute('href', h);
        });
        document.querySelectorAll('form[method="get" i][action^="/"], form:not([method])[action^="/"]').forEach(function (f) {
            if (skip.test(f.getAttribute('action')) || f.querySelector('input[name="src"]')) return;
            var input = document.createElement('input');
            input.type = 'hidden'; input.name = 'src'; input.value = c;
            f.appendChild(input);
        });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', apply); else apply();
    var tries = 0;
    var hook = function () { if (window.Blazor && Blazor.addEventListener) Blazor.addEventListener('enhancedload', apply); else if (++tries < 50) setTimeout(hook, 200); };
    hook();
})();
