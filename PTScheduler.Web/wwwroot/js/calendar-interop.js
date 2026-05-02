let calendar = null;

export function initCalendar(dotnetRef, el, canEdit) {
    const isMobile = window.innerWidth < 768;

    calendar = new FullCalendar.Calendar(el, {
        initialView: isMobile ? 'listWeek' : 'timeGridWeek',
        locale: 'pl',
        firstDay: 1,
        height: isMobile ? 'auto' : 'calc(100vh - 168px)',
        nowIndicator: true,
        slotMinTime: '06:00:00',
        slotMaxTime: '22:00:00',
        slotDuration: '00:30:00',
        slotLabelFormat: { hour: '2-digit', minute: '2-digit', hour12: false },
        expandRows: true,
        headerToolbar: {
            left: 'prev,next today',
            center: 'title',
            right: isMobile
                ? 'listDay,listWeek'
                : 'timeGridWeek,timeGridDay,dayGridMonth'
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
                const events = await dotnetRef.invokeMethodAsync('GetEvents', info.startStr, info.endStr);
                success(events);
            } catch (e) { failure(e); }
        },
        dateClick: canEdit ? info => dotnetRef.invokeMethodAsync('OnDateClick', info.dateStr) : undefined,
        eventContent: renderEventContent,
        eventClick: info => dotnetRef.invokeMethodAsync('OnEventClick', parseInt(info.event.id)),
        eventDidMount: info => {
            info.el.style.cursor = 'pointer';
            const ep = info.event.extendedProps;
            info.el.title = `${ep.clientName} — ${ep.sessionType}`;
        }
    });

    calendar.render();
}

function renderEventContent(arg) {
    const { event, view } = arg;
    const ep = event.extendedProps;
    const statusCls = 'status-' + (ep.status || 'scheduled').toLowerCase();
    const isList = view.type.startsWith('list');
    const isMonth = view.type === 'dayGridMonth';

    const initials = ep.initials || '?';
    const clientName = ep.clientName || '';
    const sessionType = ep.sessionType || '';

    if (isList) {
        return {
            html: `<div class="fc-event-card ${statusCls}" style="height:auto;padding:.3rem .5rem">
                <div class="fc-event-avatar">${initials}</div>
                <div class="fc-event-info">
                    <div class="fc-event-client">${clientName}</div>
                    <div class="fc-event-type">${sessionType}</div>
                </div>
            </div>`
        };
    }

    if (isMonth) {
        return {
            html: `<div class="fc-event-card ${statusCls}">
                <div class="fc-event-avatar">${initials}</div>
                <div class="fc-event-info">
                    <div class="fc-event-client">${clientName}</div>
                </div>
            </div>`
        };
    }

    return {
        html: `<div class="fc-event-card ${statusCls}">
            <div class="fc-event-avatar">${initials}</div>
            <div class="fc-event-info">
                <div class="fc-event-client">${clientName}</div>
                <div class="fc-event-type">${sessionType}</div>
            </div>
        </div>`
    };
}

export function refetchEvents() {
    calendar?.refetchEvents();
}

export function destroyCalendar() {
    calendar?.destroy();
    calendar = null;
}
