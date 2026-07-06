window.PTR = (() => {
    const instances = new WeakMap();
    const THRESHOLD = 60;

    function init(el, dotnet) {
        if (instances.has(el)) return;

        let startY = 0, pulling = false;

        function onTouchStart(e) {
            if (el.scrollTop > 0 || window.scrollY > 0) return;
            startY = e.touches[0].clientY;
            pulling = false;
        }

        function onTouchMove(e) {
            if (!startY) return;
            const dy = e.touches[0].clientY - startY;
            if (dy > 10 && !pulling) {
                pulling = true;
                dotnet.invokeMethodAsync('SetPulling', true);
            }
        }

        function onTouchEnd(e) {
            if (!pulling) { startY = 0; return; }
            const dy = e.changedTouches[0].clientY - startY;
            startY = 0;
            pulling = false;
            if (dy >= THRESHOLD) {
                dotnet.invokeMethodAsync('TriggerRefresh');
            } else {
                dotnet.invokeMethodAsync('SetPulling', false);
            }
        }

        el.addEventListener('touchstart', onTouchStart, { passive: true });
        el.addEventListener('touchmove', onTouchMove, { passive: true });
        el.addEventListener('touchend', onTouchEnd, { passive: true });

        instances.set(el, { onTouchStart, onTouchMove, onTouchEnd });
    }

    function destroy(el) {
        const h = instances.get(el);
        if (!h) return;
        el.removeEventListener('touchstart', h.onTouchStart);
        el.removeEventListener('touchmove', h.onTouchMove);
        el.removeEventListener('touchend', h.onTouchEnd);
        instances.delete(el);
    }

    return { init, destroy };
})();
