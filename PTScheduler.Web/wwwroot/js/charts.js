window.PTChart = (function () {
    var instances = {};
    var pending = {};

    // Gdy biblioteka Chart.js się nie wczytała (offline, blokada), wykres jest
    // pomijany — wyjątek z interopu wywracał cały obwód Blazora.
    function chartAvailable(canvasId) {
        if (typeof Chart !== 'undefined') return true;
        var c = document.getElementById(canvasId);
        if (c && c.parentElement && !c.parentElement.querySelector('.pt-chart-missing')) {
            var note = document.createElement('div');
            note.className = 'pt-chart-missing text-muted small text-center py-3';
            note.textContent = 'Nie udało się wczytać wykresu. Odśwież stronę.';
            c.parentElement.appendChild(note);
        }
        return false;
    }

    function primaryColor() {
        return getComputedStyle(document.documentElement)
            .getPropertyValue('--c-primary').trim() || '#0284C7';
    }

    function reducedMotion() {
        return window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    function destroy(canvasId) {
        if (pending[canvasId]) { pending[canvasId].disconnect(); delete pending[canvasId]; }
        if (instances[canvasId]) { instances[canvasId].destroy(); delete instances[canvasId]; }
    }

    // Wykres powstaje dopiero, gdy pojawi się na ekranie — animację widać, a nie „dzieje się” poza widokiem.
    function whenVisible(canvasId, canvas, build) {
        if (reducedMotion() || typeof IntersectionObserver === 'undefined') { build(); return; }
        var io = new IntersectionObserver(function (entries) {
            if (!entries.some(function (e) { return e.isIntersecting; })) return;
            io.disconnect();
            delete pending[canvasId];
            build();
        }, { threshold: 0.25 });
        pending[canvasId] = io;
        io.observe(canvas.parentElement || canvas);
    }

    function numbers(arr) {
        return (arr || []).map(function (v) { return v == null ? null : Number(v); });
    }

    function isEmpty(values) {
        return !values.some(function (v) { return v != null && !isNaN(v) && v !== 0; });
    }

    // ── Pusta plansza: animowany szkic wykresu zamiast białego prostokąta ──
    var emptyArt = {
        line: '<svg viewBox="0 0 300 100" preserveAspectRatio="none" aria-hidden="true">' +
            '<path class="ptc-e-line" d="M6 78 C 50 70, 70 40, 110 50 S 170 88, 205 60 S 260 22, 294 30"/>' +
            '<circle class="ptc-e-dot" cx="110" cy="50" r="5"/><circle class="ptc-e-dot d2" cx="205" cy="60" r="5"/>' +
            '<circle class="ptc-e-dot d3" cx="294" cy="30" r="5"/></svg>',
        bar: '<svg viewBox="0 0 300 100" preserveAspectRatio="none" aria-hidden="true">' +
            [18, 62, 106, 150, 194, 238].map(function (x, i) {
                var h = [40, 62, 34, 78, 52, 70][i];
                return '<rect class="ptc-e-bar" style="animation-delay:' + (i * 0.15) + 's" x="' + (x + 8) + '" y="' + (96 - h) + '" width="34" height="' + h + '" rx="6"/>';
            }).join('') + '</svg>',
        donut: '<svg viewBox="0 0 100 100" aria-hidden="true">' +
            '<circle class="ptc-e-ring-bg" cx="50" cy="50" r="34"/><circle class="ptc-e-ring" cx="50" cy="50" r="34"/></svg>'
    };

    var emptyText = {
        line: 'Wykres narysuje się sam, gdy pojawią się pierwsze dane.',
        bar: 'Słupki pojawią się, gdy będzie co policzyć.',
        donut: 'Podział pojawi się, gdy będą pierwsze dane.'
    };

    function showEmpty(canvas, kind) {
        var host = canvas.parentElement;
        if (!host) return;
        hideEmpty(canvas);
        if (getComputedStyle(host).position === 'static') host.style.position = 'relative';
        if (host.clientHeight < 140) host.style.minHeight = '180px';
        canvas.style.visibility = 'hidden';
        var box = document.createElement('div');
        box.className = 'ptc-empty ptc-empty-' + kind;
        box.innerHTML = emptyArt[kind] + '<div class="ptc-empty-text"><b>Jeszcze brak danych</b><small></small></div>';
        box.querySelector('small').textContent = canvas.getAttribute('data-empty') || emptyText[kind];
        host.appendChild(box);
    }

    function hideEmpty(canvas) {
        canvas.style.visibility = '';
        var host = canvas.parentElement;
        var old = host && host.querySelector(':scope > .ptc-empty');
        if (old) old.remove();
    }

    function prepare(canvasId, kind, values) {
        if (!chartAvailable(canvasId)) return null;
        destroy(canvasId);
        var canvas = document.getElementById(canvasId);
        if (!canvas) return null;
        if (isEmpty(values)) { showEmpty(canvas, kind); return null; }
        hideEmpty(canvas);
        return canvas;
    }

    // Linia rysuje się punkt po punkcie (każdy punkt „wyjeżdża” z poprzedniego).
    function progressiveLine(count) {
        if (reducedMotion()) return false;
        var total = Math.min(1600, 300 + count * 90);
        var step = total / Math.max(count, 1);
        function previousY(ctx) {
            if (ctx.index === 0) return ctx.chart.scales.y.getPixelForValue(ctx.chart.scales.y.min);
            var prev = ctx.chart.getDatasetMeta(ctx.datasetIndex).data[ctx.index - 1];
            return prev ? prev.getProps(['y'], true).y : 0;
        }
        return {
            x: {
                type: 'number', easing: 'easeOutQuad', duration: step, from: NaN,
                delay: function (ctx) {
                    if (ctx.type !== 'data' || ctx.xStarted) return 0;
                    ctx.xStarted = true;
                    return ctx.index * step;
                }
            },
            y: {
                type: 'number', easing: 'easeOutQuad', duration: step, from: previousY,
                delay: function (ctx) {
                    if (ctx.type !== 'data' || ctx.yStarted) return 0;
                    ctx.yStarted = true;
                    return ctx.index * step;
                }
            }
        };
    }

    return {
        renderLine: function (canvasId, labels, data, label, unit) {
            var values = numbers(data);
            var canvas = prepare(canvasId, 'line', values);
            if (!canvas) return;
            whenVisible(canvasId, canvas, function () {
                if (!document.body.contains(canvas)) return;
                var color = primaryColor();
                var ctx = canvas.getContext('2d');
                var gradient = ctx.createLinearGradient(0, 0, 0, canvas.clientHeight || 200);
                gradient.addColorStop(0, color + '44');
                gradient.addColorStop(1, color + '00');

                instances[canvasId] = new Chart(canvas, {
                    type: 'line',
                    data: {
                        labels: labels,
                        datasets: [{
                            label: label,
                            data: values,
                            borderColor: color,
                            backgroundColor: gradient,
                            borderWidth: 3,
                            fill: true,
                            tension: 0.35,
                            spanGaps: true,
                            pointRadius: values.length > 24 ? 0 : 4,
                            pointHoverRadius: 7,
                            pointBackgroundColor: '#fff',
                            pointBorderColor: color,
                            pointBorderWidth: 2
                        }]
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        animation: progressiveLine(values.length),
                        interaction: { mode: 'index', intersect: false },
                        plugins: {
                            legend: { display: false },
                            tooltip: {
                                callbacks: {
                                    label: function (c) { return ' ' + c.parsed.y + ' ' + unit; }
                                }
                            }
                        },
                        scales: {
                            x: {
                                grid: { display: false },
                                ticks: { font: { size: 11 }, maxRotation: 0, autoSkipPadding: 12 }
                            },
                            y: {
                                beginAtZero: false,
                                grid: { color: 'rgba(0,0,0,0.05)' },
                                ticks: {
                                    font: { size: 11 },
                                    callback: function (v) { return v + ' ' + unit; }
                                }
                            }
                        }
                    }
                });
            });
        },

        renderBar: function (canvasId, labels, datasets) {
            var all = [];
            (datasets || []).forEach(function (ds) { all = all.concat(numbers(ds.data)); });
            var canvas = prepare(canvasId, 'bar', all);
            if (!canvas) return;
            whenVisible(canvasId, canvas, function () {
                if (!document.body.contains(canvas)) return;
                var color = primaryColor();
                var mappedDatasets = datasets.map(function (ds, i) {
                    return {
                        label: ds.label,
                        data: numbers(ds.data),
                        backgroundColor: ds.color || (i === 0 ? color + 'CC' : color + '55'),
                        hoverBackgroundColor: ds.color || color,
                        borderRadius: 6,
                        borderSkipped: false,
                        maxBarThickness: 42
                    };
                });

                instances[canvasId] = new Chart(canvas, {
                    type: 'bar',
                    data: { labels: labels, datasets: mappedDatasets },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        // Słupki rosną po kolei, od lewej.
                        animation: reducedMotion() ? false : {
                            duration: 700,
                            easing: 'easeOutQuart',
                            delay: function (ctx) {
                                return ctx.type === 'data' && ctx.mode === 'default' ? ctx.dataIndex * 70 + ctx.datasetIndex * 140 : 0;
                            }
                        },
                        plugins: {
                            legend: { display: datasets.length > 1, position: 'top', labels: { boxWidth: 12, font: { size: 11 } } },
                            tooltip: { mode: 'index', intersect: false }
                        },
                        scales: {
                            x: { grid: { display: false }, ticks: { font: { size: 11 } } },
                            y: { beginAtZero: true, grid: { color: 'rgba(0,0,0,0.05)' }, ticks: { font: { size: 11 }, precision: 0 } }
                        }
                    }
                });
            });
        },

        renderDoughnut: function (canvasId, labels, data, colors) {
            var values = numbers(data);
            var canvas = prepare(canvasId, 'donut', values);
            if (!canvas) return;
            whenVisible(canvasId, canvas, function () {
                if (!document.body.contains(canvas)) return;
                instances[canvasId] = new Chart(canvas, {
                    type: 'doughnut',
                    data: {
                        labels: labels,
                        datasets: [{ data: values, backgroundColor: colors, borderWidth: 2, hoverOffset: 6 }]
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        cutout: '68%',
                        animation: reducedMotion() ? false : { animateRotate: true, animateScale: true, duration: 1100, easing: 'easeOutCubic' },
                        plugins: {
                            legend: { position: 'bottom', labels: { boxWidth: 12, font: { size: 11 }, padding: 12 } }
                        }
                    }
                });
            });
        },

        destroy: destroy
    };
})();
