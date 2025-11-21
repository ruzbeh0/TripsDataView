import React, { useState, useMemo, useCallback, FC } from 'react';
import useDataUpdate from 'mods/use-data-update';
import $Panel from 'mods/panel';

interface ODMatrixEntry {
    originDistrict: string;
    destinationDistrict: string;
    count: number;
}

interface ODMatrixProps {
    onClose: () => void;
}

/* Layout */
const CELL_W = 90;
const ROW_HDR_W = 160;
const CELL_H = 28;
const TOTAL_W = 110;
const PADDING = 12;
const HEADER_ROT_DEG = -90;
const FOOT_H = 34;

// Show a bit fewer rows and more columns
const ROWS_PER_PAGE = 17;
const COLS_PER_PAGE = 11;

const font = {
    family: [
        'Inter',
        '"Noto Sans CJK SC"',
        '"Noto Sans CJK JP"',
        '"Microsoft YaHei"',
        '"PingFang SC"',
        '"SimHei"',
        'system-ui',
        '-apple-system',
        'BlinkMacSystemFont',
        '"Segoe UI"',
        'Arial',
        'sans-serif',
    ].join(', '),
    size: 12,
};

function parseRGBAny(s: string): [number, number, number] | null {
    const m = s.match(/rgba?\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/i);
    return m ? [parseInt(m[1], 10), parseInt(m[2], 10), parseInt(m[3], 10)] : null;
}

function pickTextColor(bg: string): 'white' | 'black' {
    const rgb = parseRGBAny(bg);
    if (!rgb) return 'white';
    const [r8, g8, b8] = rgb;
    const brightness = (r8 * 299 + g8 * 587 + b8 * 114) / 1000;
    if (brightness >= 160) return 'black';

    const [r, g, b] = [r8, g8, b8].map((v) => {
        const s = v / 255;
        return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
    });
    const L = 0.2126 * r + 0.7152 * g + 0.0722 * b;
    return L < 0.5 ? 'white' : 'black';
}

const ODMatrix: FC<ODMatrixProps> = ({ onClose }) => {
    const [entries, setEntries] = useState<ODMatrixEntry[]>([]);
    const [districts, setDistricts] = useState<string[]>([]);
    const [hideIntra, setHideIntra] = useState<boolean>(false);
    const [rowPage, setRowPage] = useState<number>(0);
    const [colPage, setColPage] = useState<number>(0);

    useDataUpdate('odMatrixInfo.odMatrixDetails', (data: ODMatrixEntry[]) => {
        setEntries(Array.isArray(data) ? data : []);
    });

    useDataUpdate('odMatrixInfo.districtList', (data: string[]) => {
        const list = Array.isArray(data) ? data.slice() : [];
        const idx = list.indexOf('Other');
        if (idx >= 0) {
            list.splice(idx, 1);
            list.push('Other');
        }
        setDistricts(list);
        setRowPage(0);
        setColPage(0);
    });

    const filteredEntries = useMemo(
        () =>
            hideIntra
                ? entries.filter((e) => e.originDistrict !== e.destinationDistrict)
                : entries,
        [entries, hideIntra],
    );

    const matrix = useMemo(() => {
        const m = new Map<string, Map<string, number>>();
        districts.forEach((o) => {
            const row = new Map<string, number>();
            districts.forEach((d) => row.set(d, 0));
            m.set(o, row);
        });
        filteredEntries.forEach(({ originDistrict, destinationDistrict, count }) => {
            if (!m.has(originDistrict)) {
                const row = new Map<string, number>();
                districts.forEach((d) => row.set(d, 0));
                m.set(originDistrict, row);
            }
            m.get(originDistrict)!.set(destinationDistrict, count);
        });
        return m;
    }, [filteredEntries, districts]);

    const { min, max } = useMemo(() => {
        let mi = Infinity;
        let ma = -Infinity;
        matrix.forEach((r) =>
            r.forEach((c) => {
                if (c > 0) {
                    mi = Math.min(mi, c);
                    ma = Math.max(ma, c);
                }
            }),
        );
        if (!isFinite(mi)) mi = 0;
        if (!isFinite(ma)) ma = 0;
        return { min: mi, max: ma };
    }, [matrix]);

    const getColor = useCallback(
        (v: number) => {
            if (v === 0) return 'rgba(255,255,255,0.10)';
            if (max === min) return 'rgb(0,128,0)';
            let n = (v - min) / (max - min);
            if (n < 0) n = 0;
            if (n > 1) n = 1;
            const idx = Math.min(9, Math.floor(n * 10));
            if (idx <= 4) {
                const t = idx / 4;
                const r = Math.round(255 * t);
                const g = 255;
                return `rgb(${r}, ${g}, 0)`;
            } else {
                const t = (idx - 5) / 4;
                const r = 255;
                const g = Math.round(255 * (1 - t));
                return `rgb(${r}, ${g}, 0)`;
            }
        },
        [min, max],
    );

    const HDR_H = useMemo(() => {
        if (districts.length === 0) return 50;
        const longest = districts.reduce((m, d) => Math.max(m, d.length), 0);
        return Math.max(50, Math.min(600, Math.round(longest * font.size * 0.6 + 14)));
    }, [districts]);

    const totalRowPages = Math.max(1, Math.ceil(districts.length / ROWS_PER_PAGE));
    const totalColPages = Math.max(1, Math.ceil(districts.length / COLS_PER_PAGE));

    const visibleRows = useMemo(() => {
        const start = rowPage * ROWS_PER_PAGE;
        const end = Math.min(districts.length, start + ROWS_PER_PAGE);
        return districts.slice(start, end);
    }, [districts, rowPage]);

    const visibleCols = useMemo(() => {
        const start = colPage * COLS_PER_PAGE;
        const end = Math.min(districts.length, start + COLS_PER_PAGE);
        return districts.slice(start, end);
    }, [districts, colPage]);

    const rowTotals = useMemo(() => {
        const t = new Map<string, number>();
        visibleRows.forEach((o) => {
            let s = 0;
            visibleCols.forEach((d) => {
                s += matrix.get(o)?.get(d) ?? 0;
            });
            t.set(o, s);
        });
        return t;
    }, [visibleRows, visibleCols, matrix]);

    const colTotals = useMemo(() => {
        const t = new Map<string, number>();
        visibleCols.forEach((d) => t.set(d, 0));
        visibleRows.forEach((o) => {
            visibleCols.forEach((d) => {
                const v = matrix.get(o)?.get(d) ?? 0;
                t.set(d, (t.get(d) || 0) + v);
            });
        });
        return t;
    }, [visibleRows, visibleCols, matrix]);

    const grandTotal = useMemo(
        () => Array.from(rowTotals.values()).reduce((a, b) => a + b, 0),
        [rowTotals],
    );

    const rows = visibleRows.length;
    const cols = visibleCols.length;

    const innerWidth = ROW_HDR_W + cols * CELL_W + TOTAL_W;
    const innerHeight = HDR_H + rows * CELL_H + FOOT_H;
    const svgWidth = innerWidth + PADDING * 2;
    const svgHeight = innerHeight + PADDING * 2;

    const panWidth = Math.round(window.innerWidth * 0.7);
    const panHeight = Math.round(window.innerHeight * 0.75);

    const handleClose = useCallback(() => onClose(), [onClose]);

    const handlePrevRowPage = useCallback(() => {
        setRowPage((p) => Math.max(0, p - 1));
    }, []);

    const handleNextRowPage = useCallback(() => {
        setRowPage((p) => Math.min(totalRowPages - 1, p + 1));
    }, [totalRowPages]);

    const handlePrevColPage = useCallback(() => {
        setColPage((p) => Math.max(0, p - 1));
    }, []);

    const handleNextColPage = useCallback(() => {
        setColPage((p) => Math.min(totalColPages - 1, p + 1));
    }, [totalColPages]);

    return (
        <$Panel
            title="Origin Destination Matrix by Districts"
            onClose={handleClose}
            initialSize={{ width: panWidth, height: panHeight }}
            initialPosition={{
                top: window.innerHeight * 0.1,
                left: window.innerWidth * 0.15,
            }}
            style={{
                backgroundColor: 'var(--panelColorNormal)',
                display: 'flex',
                flexDirection: 'column',
            }}
        >
            {/* Controls */}
            <div
                style={{
                    padding: '0.5rem 1rem 0 1rem',
                    display: 'flex',
                    flexWrap: 'wrap',
                    alignItems: 'center',
                    color: 'white',
                    fontFamily: font.family,
                    fontSize: font.size,
                }}
            >
                {/* Intra-district toggle */}
                <button
                    onClick={() => setHideIntra((v) => !v)}
                    style={{
                        padding: '0.4rem 0.9rem',
                        borderRadius: '999px',
                        border: '2px solid #ffffff',
                        backgroundColor: hideIntra ? 'rgba(255,255,255,0.18)' : 'rgba(0,0,0,0.55)',
                        cursor: 'pointer',
                        fontFamily: font.family,
                        fontSize: font.size,
                        fontWeight: 'bold',
                        color: 'white',
                        marginRight: '1.5rem',
                        marginBottom: '0.5rem',
                    }}
                >
                    Hide intra-district trips (O = D)
                </button>

                {/* Row pager */}
                <div
                    style={{
                        display: 'flex',
                        alignItems: 'center',
                        padding: '0.25rem 0.8rem',
                        borderRadius: '999px',
                        backgroundColor: 'rgba(0,0,0,0.5)',
                        border: '1px solid rgba(255,255,255,0.35)',
                        marginRight: '1.5rem',
                        marginBottom: '0.5rem',
                    }}
                >
                    <span style={{ marginRight: '0.5rem' }}>Rows:</span>
                    <button
                        onClick={handlePrevRowPage}
                        disabled={rowPage === 0}
                        style={{
                            padding: '0.15rem 0.6rem',
                            borderRadius: '999px',
                            border: '1px solid rgba(255,255,255,0.7)',
                            backgroundColor:
                                rowPage === 0 ? 'rgba(255,255,255,0.12)' : 'rgba(0,0,0,0.7)',
                            color: 'white',
                            cursor: rowPage === 0 ? 'default' : 'pointer',
                            marginRight: '0.6rem',
                        }}
                    >
                        ◀ Prev
                    </button>
                    <span style={{ minWidth: '3.5rem', textAlign: 'center' }}>
                        {Math.min(rowPage + 1, totalRowPages)} / {totalRowPages}
                    </span>
                    <button
                        onClick={handleNextRowPage}
                        disabled={rowPage >= totalRowPages - 1}
                        style={{
                            padding: '0.15rem 0.6rem',
                            borderRadius: '999px',
                            border: '1px solid rgba(255,255,255,0.7)',
                            backgroundColor:
                                rowPage >= totalRowPages - 1
                                    ? 'rgba(255,255,255,0.12)'
                                    : 'rgba(0,0,0,0.7)',
                            color: 'white',
                            cursor: rowPage >= totalRowPages - 1 ? 'default' : 'pointer',
                            marginLeft: '0.6rem',
                        }}
                    >
                        Next ▶
                    </button>
                </div>

                {/* Column pager */}
                <div
                    style={{
                        display: 'flex',
                        alignItems: 'center',
                        padding: '0.25rem 0.8rem',
                        borderRadius: '999px',
                        backgroundColor: 'rgba(0,0,0,0.5)',
                        border: '1px solid rgba(255,255,255,0.35)',
                        marginBottom: '0.5rem',
                    }}
                >
                    <span style={{ marginRight: '0.5rem' }}>Columns:</span>
                    <button
                        onClick={handlePrevColPage}
                        disabled={colPage === 0}
                        style={{
                            padding: '0.15rem 0.6rem',
                            borderRadius: '999px',
                            border: '1px solid rgba(255,255,255,0.7)',
                            backgroundColor:
                                colPage === 0 ? 'rgba(255,255,255,0.12)' : 'rgba(0,0,0,0.7)',
                            color: 'white',
                            cursor: colPage === 0 ? 'default' : 'pointer',
                            marginRight: '0.6rem',
                        }}
                    >
                        ◀ Prev
                    </button>
                    <span style={{ minWidth: '3.5rem', textAlign: 'center' }}>
                        {Math.min(colPage + 1, totalColPages)} / {totalColPages}
                    </span>
                    <button
                        onClick={handleNextColPage}
                        disabled={colPage >= totalColPages - 1}
                        style={{
                            padding: '0.15rem 0.6rem',
                            borderRadius: '999px',
                            border: '1px solid rgba(255,255,255,0.7)',
                            backgroundColor:
                                colPage >= totalColPages - 1
                                    ? 'rgba(255,255,255,0.12)'
                                    : 'rgba(0,0,0,0.7)',
                            color: 'white',
                            cursor: colPage >= totalColPages - 1 ? 'default' : 'pointer',
                            marginLeft: '0.6rem',
                        }}
                    >
                        Next ▶
                    </button>
                </div>
            </div>

            {/* Paged matrix */}
            <div style={{ flex: '1 1 auto', padding: '1rem' }}>
                <svg
                    width={svgWidth}
                    height={svgHeight}
                    viewBox={`0 0 ${svgWidth} ${svgHeight}`}
                    style={{ display: 'block' }}
                >
                    <rect x={0} y={0} width={svgWidth} height={svgHeight} fill="rgba(0,0,0,0.3)" />
                    <g transform={`translate(${PADDING}, ${PADDING})`}>
                        <rect x={0} y={0} width={innerWidth} height={HDR_H} fill="rgba(30,30,30,0.95)" />
                        <rect
                            x={0}
                            y={HDR_H + rows * CELL_H}
                            width={innerWidth}
                            height={FOOT_H}
                            fill="rgba(30,30,30,0.95)"
                        />
                        <rect x={0} y={0} width={ROW_HDR_W} height={innerHeight} fill="rgba(30,30,30,0.95)" />
                        <rect
                            x={ROW_HDR_W + cols * CELL_W}
                            y={0}
                            width={TOTAL_W}
                            height={innerHeight}
                            fill="rgba(30,30,30,0.95)"
                        />
                        <rect
                            x={0.5}
                            y={0.5}
                            width={innerWidth - 1}
                            height={innerHeight - 1}
                            fill="none"
                            stroke="#555"
                        />

                        {/* Top-left label */}
                        <text
                            x={8}
                            y={HDR_H / 2 + 4}
                            fontFamily={font.family}
                            fontSize={font.size}
                            fill="white"
                            fontWeight="bold"
                        >
                            Origin \ Destination
                        </text>

                        {/* Destination headers */}
                        {visibleCols.map((d, j) => {
                            const cx = ROW_HDR_W + j * CELL_W + CELL_W / 2;
                            const cy = HDR_H / 2;
                            const fitLen = HDR_H - 12;
                            return (
                                <g key={`h-${d}`}>
                                    <line
                                        x1={ROW_HDR_W + j * CELL_W}
                                        y1={0}
                                        x2={ROW_HDR_W + j * CELL_W}
                                        y2={innerHeight}
                                        stroke="#555"
                                    />
                                    <g transform={`translate(${cx}, ${cy}) rotate(${HEADER_ROT_DEG})`}>
                                        <text
                                            x={0}
                                            y={0}
                                            fontFamily={font.family}
                                            fontSize={font.size}
                                            fill="white"
                                            fontWeight="bold"
                                            textAnchor="middle"
                                            dominantBaseline="middle"
                                            lengthAdjust="spacingAndGlyphs"
                                            textLength={fitLen}
                                            style={{ pointerEvents: 'none' }}
                                        >
                                            {d}
                                        </text>
                                    </g>
                                </g>
                            );
                        })}

                        {/* Line before totals col */}
                        <line
                            x1={ROW_HDR_W + cols * CELL_W}
                            y1={0}
                            x2={ROW_HDR_W + cols * CELL_W}
                            y2={innerHeight}
                            stroke="#555"
                        />

                        {/* Row total header */}
                        <text
                            x={ROW_HDR_W + cols * CELL_W + TOTAL_W / 2}
                            y={HDR_H / 2 + 4}
                            fontFamily={font.family}
                            fontSize={font.size}
                            fill="white"
                            fontWeight="bold"
                            textAnchor="middle"
                        >
                            Row total
                        </text>

                        {/* Rows + cells */}
                        {visibleRows.map((o, i) => {
                            const y = HDR_H + i * CELL_H;
                            return (
                                <g key={`r-${o}`}>
                                    <line x1={0} y1={y} x2={innerWidth} y2={y} stroke="#555" />
                                    <text
                                        x={8}
                                        y={y + CELL_H / 2 + 4}
                                        fontFamily={font.family}
                                        fontSize={font.size}
                                        fill="white"
                                        fontWeight="bold"
                                    >
                                        {o}
                                    </text>

                                    {visibleCols.map((d, j) => {
                                        const x = ROW_HDR_W + j * CELL_W;
                                        const count = matrix.get(o)?.get(d) ?? 0;
                                        const bg = getColor(count);
                                        const txt = pickTextColor(bg);
                                        return (
                                            <g key={`c-${o}-${d}`}>
                                                <rect
                                                    x={x}
                                                    y={y}
                                                    width={CELL_W}
                                                    height={CELL_H}
                                                    fill={bg}
                                                    stroke="#555"
                                                />
                                                <text
                                                    x={x + CELL_W / 2}
                                                    y={y + CELL_H / 2 + 4}
                                                    fontFamily={font.family}
                                                    fontSize={font.size}
                                                    fill={txt}
                                                    fontWeight={count > 0 ? 'bold' : 'normal'}
                                                    textAnchor="middle"
                                                >
                                                    {count}
                                                </text>
                                            </g>
                                        );
                                    })}

                                    {/* Row total cell */}
                                    <g>
                                        <rect
                                            x={ROW_HDR_W + cols * CELL_W}
                                            y={y}
                                            width={TOTAL_W}
                                            height={CELL_H}
                                            fill="rgba(30,30,30,0.95)"
                                            stroke="#555"
                                        />
                                        <text
                                            x={ROW_HDR_W + cols * CELL_W + TOTAL_W / 2}
                                            y={y + CELL_H / 2 + 4}
                                            fontFamily={font.family}
                                            fontSize={font.size}
                                            fill="white"
                                            fontWeight="bold"
                                            textAnchor="middle"
                                        >
                                            {rowTotals.get(o)}
                                        </text>
                                    </g>
                                </g>
                            );
                        })}

                        {/* Column totals */}
                        <line
                            x1={0}
                            y1={HDR_H + rows * CELL_H}
                            x2={innerWidth}
                            y2={HDR_H + rows * CELL_H}
                            stroke="#555"
                        />
                        <text
                            x={8}
                            y={HDR_H + rows * CELL_H + FOOT_H / 2 + 4}
                            fontFamily={font.family}
                            fontSize={font.size}
                            fill="white"
                            fontWeight="bold"
                        >
                            Column total
                        </text>
                        {visibleCols.map((d, j) => {
                            const x = ROW_HDR_W + j * CELL_W;
                            return (
                                <g key={`t-${d}`}>
                                    <rect
                                        x={x}
                                        y={HDR_H + rows * CELL_H}
                                        width={CELL_W}
                                        height={FOOT_H}
                                        fill="rgba(30,30,30,0.95)"
                                        stroke="#555"
                                    />
                                    <text
                                        x={x + CELL_W / 2}
                                        y={HDR_H + rows * CELL_H + FOOT_H / 2 + 4}
                                        fontFamily={font.family}
                                        fontSize={font.size}
                                        fill="white"
                                        fontWeight="bold"
                                        textAnchor="middle"
                                    >
                                        {colTotals.get(d)}
                                    </text>
                                </g>
                            );
                        })}

                        {/* Grand total */}
                        <g>
                            <rect
                                x={ROW_HDR_W + cols * CELL_W}
                                y={HDR_H + rows * CELL_H}
                                width={TOTAL_W}
                                height={FOOT_H}
                                fill="rgba(30,30,30,0.95)"
                                stroke="#555"
                            />
                            <text
                                x={ROW_HDR_W + cols * CELL_W + TOTAL_W / 2}
                                y={HDR_H + rows * CELL_H + FOOT_H / 2 + 4}
                                fontFamily={font.family}
                                fontSize={font.size}
                                fill="white"
                                fontWeight="bold"
                                textAnchor="middle"
                            >
                                {grandTotal}
                            </text>
                        </g>
                    </g>
                </svg>
            </div>
        </$Panel>
    );
};

export default ODMatrix;
