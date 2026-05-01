let calendar = null;

export function initCalendar(dotnetRef, el) {
    const isMobile = window.innerWidth < 768;

    calendar = new FullCalendar.Calendar(el, {
        initialView: isMobile ? 'listWeek' : 'timeGridWeek',
        locale: 'pl',
        firstDay: 1,
        height: 'auto',
        nowIndicator: true,
        slotMinTime: '06:00:00',
        slotMaxTime: '22:00:00',
        slotDuration: '00:30:00',
        headerToolbar: {
            left: 'prev,next today',
            center: 'title',
            right: isMobile
                ? 'listDay,listWeek'
                : 'dayGridMonth,timeGridWeek,timeGridDay'
        },
        buttonText: {
            today: 'Dziś',
            month: 'Miesiąc',
            week: 'Tydzień',
            day: 'Dzień',
            list: 'Lista'
        },
        events: async (info, success, failure) => {
            try {
                const events = await dotnetRef.invokeMethodAsync('GetEvents',
                    info.startStr, info.endStr);
                success(events);
            } catch (e) { failure(e); }
        },
        dateClick: info => dotnetRef.invokeMethodAsync('OnDateClick', info.dateStr),
        eventClick: info => dotnetRef.invokeMethodAsync('OnEventClick', parseInt(info.event.id)),
        eventDidMount: info => {
            info.el.style.cursor = 'pointer';
        }
    });

    calendar.render();
}

export function refetchEvents() {
    calendar?.refetchEvents();
}

export function destroyCalendar() {
    calendar?.destroy();
    calendar = null;
}
