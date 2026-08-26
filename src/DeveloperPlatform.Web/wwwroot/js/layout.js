// Reports the viewport width to Blazor so the layout can switch between a
// persistent (desktop) and temporary (mobile) navigation drawer. We manage
// this ourselves rather than relying on MudBlazor's responsive drawer, whose
// breakpoint observer does not clear the drawer's `--initial` display:none
// gate in this Blazor Server setup, leaving the drawer stuck hidden on mobile.
window.devPlatformLayout = {
    _handler: null,

    init: function (dotnetRef) {
        this.dispose();
        let frame = null;
        const report = () => {
            if (frame) return;
            frame = requestAnimationFrame(() => {
                frame = null;
                dotnetRef.invokeMethodAsync('OnViewportWidthChanged', window.innerWidth);
            });
        };
        this._handler = report;
        window.addEventListener('resize', report);
        report();
    },

    dispose: function () {
        if (this._handler) {
            window.removeEventListener('resize', this._handler);
            this._handler = null;
        }
    }
};
