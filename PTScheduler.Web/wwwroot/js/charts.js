window.PTChart = (function () {
    var instances = {};

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

    return {
        renderLine: function (canvasId, labels, data, label, unit) {
            if (!chartAvailable(canvasId)) return;
            if (instances[canvasId]) {
                instances[canvasId].destroy();
                delete instances[canvasId];
            }
            var canvas = document.getElementById(canvasId);
            if (!canvas) return;

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
                        data: data,
                        borderColor: color,
                        backgroundColor: gradient,
                        fill: true,
                        tension: 0.35,
                        pointRadius: 4,
                        pointHoverRadius: 7,
                        pointBackgroundColor: '#fff',
                        pointBorderColor: color,
                        pointBorderWidth: 2
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            callbacks: {
                                label: function (ctx) { return ' ' + ctx.parsed.y + ' ' + unit; }
                            }
                        }
                    },
                    scales: {
                        x: {
                            grid: { display: false },
                            ticks: { font: { size: 11 }, maxRotation: 0 }
                        },
                        y: {
                            beginAtZero: false,
                            grid: { color: 'rgba(0,0,0,0.05)' },
                            ticks: {
                                font: { size: 11 },
                                callback: function (v) { return v + ' ' + unit; }
                            }
                        }
                    }
                }
            });
        },

        renderBar: function (canvasId, labels, datasets) {
            if (!chartAvailable(canvasId)) return;
            if (instances[canvasId]) {
                instances[canvasId].destroy();
                delete instances[canvasId];
            }
            var canvas = document.getElementById(canvasId);
            if (!canvas) return;

            var color = primaryColor();
            var mappedDatasets = datasets.map(function (ds, i) {
                return {
                    label: ds.label,
                    data: ds.data,
                    backgroundColor: ds.color || (i === 0 ? color + 'CC' : color + '55'),
                    borderRadius: 4,
                    borderSkipped: false
                };
            });

            instances[canvasId] = new Chart(canvas, {
                type: 'bar',
                data: { labels: labels, datasets: mappedDatasets },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
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
        },

        renderDoughnut: function (canvasId, labels, data, colors) {
            if (!chartAvailable(canvasId)) return;
            if (instances[canvasId]) {
                instances[canvasId].destroy();
                delete instances[canvasId];
            }
            var canvas = document.getElementById(canvasId);
            if (!canvas) return;

            instances[canvasId] = new Chart(canvas, {
                type: 'doughnut',
                data: {
                    labels: labels,
                    datasets: [{ data: data, backgroundColor: colors, borderWidth: 2, hoverOffset: 4 }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    cutout: '68%',
                    plugins: {
                        legend: { position: 'bottom', labels: { boxWidth: 12, font: { size: 11 }, padding: 12 } }
                    }
                }
            });
        },

        destroy: function (canvasId) {
            if (instances[canvasId]) {
                instances[canvasId].destroy();
                delete instances[canvasId];
            }
        }
    };
})();
