(function () {
    var api = window["cs2/api"];
    if (!api) { return; }

    var realm = window.__chRealm || (window.__chRealm = Math.random().toString(36).slice(2, 7));

    if (window.__customHouseholdsSection) {
        try { api.trigger("CustomHouseholds", "injected", "cached:" + realm); } catch (e) { }
        return;
    }

    var modding = window["cs2/modding"];
    var React = window.React;
    if (!modding || !React) { return; }

    try {
        var components = modding.getModule(
            "game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx",
            "selectedInfoSectionComponents");
        if (!components) {
            api.trigger("CustomHouseholds", "jserr", "realm " + realm + ": selectedInfoSectionComponents not found");
            return;
        }

        var InfoSection = null;
        try {
            InfoSection = modding.getModule(
                "game-ui/game/components/selected-info-panel/shared-components/info-section/info-section.tsx",
                "InfoSection");
        } catch (eI) { InfoSection = null; }

        var bVisible = api.bindValue("CustomHouseholds", "visible", false);
        var bCurrent = api.bindValue("CustomHouseholds", "current", 0);
        var bOverridden = api.bindValue("CustomHouseholds", "overridden", false);
        var bOriginal = api.bindValue("CustomHouseholds", "original", -1);

        var e = React.createElement;

        var headerRowStyle = { display: "flex", alignItems: "baseline" };
        var titleStyle = {
            flexGrow: 1, flexShrink: 1, minWidth: 0,
            textTransform: "uppercase", letterSpacing: "0.5px",
            fontSize: "12px", fontWeight: "bold", opacity: 0.85
        };
        var badgeStyle = { flexShrink: 0, fontSize: "12px", opacity: 0.7, marginLeft: "8px" };
        var rowStyle = { display: "flex", alignItems: "center", marginTop: "6px" };
        var labelStyle = { flexGrow: 1, flexShrink: 1, minWidth: 0, marginRight: "8px" };
        var inputStyle = {
            width: "56px", textAlign: "center", boxSizing: "border-box",
            background: "rgba(0,0,0,0.35)", color: "inherit",
            border: "1px solid rgba(255,255,255,0.25)", borderRadius: "4px",
            padding: "2px 4px", font: "inherit", marginRight: "6px"
        };
        var btnStyle = {
            padding: "2px 10px", borderRadius: "4px", border: "none",
            background: "rgba(255,255,255,0.18)", color: "inherit",
            font: "inherit", cursor: "pointer", marginLeft: "4px"
        };
        var btnDisabledStyle = {
            padding: "2px 10px", borderRadius: "4px", border: "none",
            background: "rgba(255,255,255,0.06)", color: "rgba(255,255,255,0.35)",
            font: "inherit", marginLeft: "4px"
        };

        function HouseholdOverrideSection() {
            var visible = api.useValue(bVisible);
            var current = api.useValue(bCurrent);
            var overridden = api.useValue(bOverridden);
            var original = api.useValue(bOriginal);

            var pair = React.useState("");
            var text = pair[0];
            var setText = pair[1];

            if (!window.__chRendered) {
                window.__chRendered = true;
                try { api.trigger("CustomHouseholds", "jsdbg", "section rendered (realm " + realm + ")"); } catch (e0) { }
            }

            React.useEffect(function () {
                setText(String(current));
            }, [current]);

            if (!visible) { return null; }

            function apply() {
                var v = parseInt(text, 10);
                if (!isNaN(v)) {
                    api.trigger("CustomHouseholds", "apply", v);
                }
            }

            function reset() {
                api.trigger("CustomHouseholds", "reset");
            }

            var badge = overridden
                ? (original > 0 ? ("Overridden (original: " + original + ")") : "Overridden")
                : "Default";

            var header = e("div", { style: headerRowStyle },
                e("div", { style: titleStyle }, "Custom Households"),
                e("div", { style: badgeStyle }, badge));
            var row = e("div", { style: rowStyle },
                e("span", { style: labelStyle }, "New Total Households"),
                e("input", {
                    type: "number", min: 1, max: 2000, value: text,
                    style: inputStyle,
                    onChange: function (ev) { setText(ev.target.value); },
                    onKeyDown: function (ev) {
                        if (ev.key === "Enter" || ev.keyCode === 13) { apply(); }
                    }
                }),
                e("button", { style: btnStyle, onClick: apply }, "Apply"),
                e("button", {
                    style: overridden ? btnStyle : btnDisabledStyle,
                    onClick: overridden ? reset : undefined
                }, "Reset"));

            if (InfoSection) {
                return e(InfoSection, null, header, row);
            }
            return e("div", { style: { padding: "6px" } }, header, row);
        }

        components["CustomHouseholds.HouseholdOverrideSection"] = HouseholdOverrideSection;
        window.__customHouseholdsSection = true;
        api.trigger("CustomHouseholds", "injected", "fresh:" + realm);
    } catch (err) {
        try {
            api.trigger("CustomHouseholds", "jserr", "realm " + realm + ": " + String(err && err.message ? err.message : err));
        } catch (e2) { }
    }
})();
