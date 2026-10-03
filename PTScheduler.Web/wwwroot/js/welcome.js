// Welcome-page animations: scroll reveal + stat counters.
(function () {
    function animateCount(el) {
        var target = parseFloat(el.getAttribute('data-count'));
        if (isNaN(target)) return;
        var suffix = el.getAttribute('data-suffix') || '';
        var prefix = el.getAttribute('data-prefix') || '';
        var dur = 1300, start = null;
        function step(ts) {
            if (!start) start = ts;
            var p = Math.min((ts - start) / dur, 1);
            var eased = 1 - Math.pow(1 - p, 3);
            var val = Math.round(target * eased);
            el.textContent = prefix + val + suffix;
            if (p < 1) requestAnimationFrame(step);
            else el.textContent = prefix + target + suffix;
        }
        el.textContent = prefix + '0' + suffix;
        requestAnimationFrame(step);
    }

    function init() {
        var reveals = document.querySelectorAll('.wp-reveal:not(.wp-revealed)');
        if (!('IntersectionObserver' in window)) {
            // No observer support — just show everything.
            reveals.forEach(function (el) { el.classList.add('wp-revealed'); });
            document.querySelectorAll('.wp-count[data-count]').forEach(animateCount);
            return;
        }
        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (e) {
                if (e.isIntersecting) {
                    e.target.classList.add('wp-revealed');
                    e.target.querySelectorAll('.wp-count[data-count]').forEach(animateCount);
                    io.unobserve(e.target);
                }
            });
        }, { threshold: 0.15 });
        reveals.forEach(function (el) { io.observe(el); });
    }

    window.addEventListener('DOMContentLoaded', init);
    document.addEventListener('enhancedload', init);
    if (document.readyState !== 'loading') init();
})();
