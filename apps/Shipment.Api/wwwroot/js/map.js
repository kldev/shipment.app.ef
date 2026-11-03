// The map of Components/Ui/Map.razor: Leaflet (wwwroot/lib/leaflet, a global L) over OpenStreetMap tiles. One object
// per component; Blazor keeps it as an IJSObjectReference and calls its methods. Markers are circles in the theme's
// colours (no image files); a picker turns a click on the map into coordinates for the form.

const tiles = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
const attribution = '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors';

// Kind -> colours of the circle (outline, fill). The names match MapMarker.Kind in C#.
const styles = {
    pickup: { color: "#1e4f8f", fillColor: "#2a78d6" },
    customer: { color: "#b4461b", fillColor: "#eb6834" },
    courier: { color: "#15803d", fillColor: "#22c55e" },
    picked: { color: "#7c3aed", fillColor: "#a78bfa" },
    muted: { color: "#64748b", fillColor: "#94a3b8" },
};

// Kind -> style of a route line (MapLine.Kind in C#): the way ahead, the way driven, a straight-line guess.
const lineStyles = {
    route: { color: "#2a78d6", weight: 4, opacity: 0.75, dashArray: "8 8" },
    driven: { color: "#15803d", weight: 5, opacity: 0.9 },
    approximate: { color: "#64748b", weight: 3, opacity: 0.7, dashArray: "2 8" },
};

function text(value) {
    const span = document.createElement("span");
    span.style.whiteSpace = "pre-line";
    span.textContent = value;
    return span;
}

export function create(element, dotnet, options) {
    const map = L.map(element, { zoomControl: true, attributionControl: true }).setView(
        [options.latitude, options.longitude],
        options.zoom,
    );
    L.tileLayer(tiles, { maxZoom: 19, attribution }).addTo(map);
    // Lines under the markers: their own layer, redrawn on their own.
    const lines = L.layerGroup().addTo(map);
    const layer = L.layerGroup().addTo(map);
    let picked = null;
    let picking = false;

    map.on("click", (e) => {
        if (!picking) return;
        const lat = Math.round(e.latlng.lat * 1e6) / 1e6;
        const lng = Math.round(e.latlng.lng * 1e6) / 1e6;
        showPicked(lat, lng);
        dotnet.invokeMethodAsync("Picked", lat, lng);
    });

    function showPicked(lat, lng) {
        if (picked) picked.remove();
        picked = L.circleMarker([lat, lng], { radius: 10, weight: 3, fillOpacity: 0.6, ...styles.picked }).addTo(map);
    }

    // The container may get its size after the first render (a modal, a tab): measure again before fitting.
    const resize = new ResizeObserver(() => map.invalidateSize());
    resize.observe(element);

    return {
        setMarkers(markers, fit) {
            layer.clearLayers();
            const points = [];
            for (const m of markers) {
                const style = styles[m.kind] ?? styles.muted;
                const circle = L.circleMarker([m.latitude, m.longitude], {
                    radius: 8,
                    weight: 2,
                    fillOpacity: 0.85,
                    ...style,
                }).addTo(layer);
                // Names come from the database: text nodes, never HTML.
                circle.bindTooltip(text(m.label));
                if (m.popup) circle.bindPopup(text(m.popup));
                points.push([m.latitude, m.longitude]);
            }
            if (fit && points.length === 1) map.setView(points[0], 15);
            else if (fit && points.length > 1) map.fitBounds(points, { padding: [32, 32], maxZoom: 15 });
        },
        setLines(items, fit) {
            lines.clearLayers();
            const points = [];
            for (const line of items) {
                if (line.points.length < 2) continue;
                L.polyline(line.points, lineStyles[line.kind] ?? lineStyles.route).addTo(lines);
                points.push(...line.points);
            }
            if (fit && points.length > 1) map.fitBounds(points, { padding: [32, 32], maxZoom: 16 });
        },
        setPicker(enabled, latitude, longitude) {
            picking = enabled;
            element.style.cursor = enabled ? "crosshair" : "";
            if (picked) {
                picked.remove();
                picked = null;
            }
            if (enabled && latitude != null && longitude != null) {
                showPicked(latitude, longitude);
                map.setView([latitude, longitude], Math.max(map.getZoom(), 15));
            }
        },
        dispose() {
            resize.disconnect();
            map.remove();
        },
    };
}
