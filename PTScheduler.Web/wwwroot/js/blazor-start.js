// Start Blazora z szybkim wznawianiem połączenia: po wybudzeniu telefonu pierwsze próby idą od razu
// i co chwilę (zamiast dłuższych odstępów), a ReconnectModal.razor.js robi to bez okna „Łączę z serwerem…”.
(function () {
    var quick = [0, 300, 1000, 2000, 3000];
    Blazor.start({
        circuit: {
            reconnectionOptions: {
                maxRetries: 60,
                retryIntervalMilliseconds: function (previousAttempts) {
                    return previousAttempts < quick.length ? quick[previousAttempts] : 5000;
                }
            }
        }
    });
})();
