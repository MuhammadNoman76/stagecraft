// Generates Stagecraft's icon resources from Microsoft's Fluent UI System Icons (MIT licence),
// package @fluentui/svg-icons (vector SVGs). No icon font is used, so the icons render on Windows 7 too.
//
//   npm pack @fluentui/svg-icons@1.1.344            (in an empty scratch folder)
//   tar -xzf fluentui-svg-icons-1.1.344.tgz
//   node tools/make-icons.mjs <scratch>/package
//
// Writes:
//   src/Stagecraft/Themes/Icons.xaml                 the app's own UI icons (20 x 20, Geometry resources "Icon.Name")
//   src/Stagecraft.Core/Assets/IconCatalog.txt       the icons people can put on slides (24 x 24), one per line:
//                                                    key <TAB> extra search words <TAB> path data
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const pkg = process.argv[2];
if (!pkg || !fs.existsSync(path.join(pkg, 'icons'))) {
  console.error('usage: node tools/make-icons.mjs <path to the extracted @fluentui/svg-icons package>');
  process.exit(1);
}
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const version = JSON.parse(fs.readFileSync(path.join(pkg, 'package.json'), 'utf8')).version;
const iconsDir = path.join(pkg, 'icons');

// ------------------------------------------------------------------ SVG path data -> WPF path markup

const arity = { m: 2, l: 2, h: 1, v: 1, c: 6, s: 4, q: 4, t: 2, a: 7, z: 0 };

function parsePath(d) {
  const out = [];
  let i = 0;
  const n = d.length;
  const skip = () => { while (i < n && /[\s,]/.test(d[i])) i++; };
  const number = () => {
    skip();
    const m = /^[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?/.exec(d.slice(i));
    if (!m) throw new Error('number expected at ' + i + ' in ' + d.slice(i, i + 20));
    i += m[0].length;
    return parseFloat(m[0]);
  };
  const flag = () => {
    skip();
    const c = d[i];
    if (c !== '0' && c !== '1') throw new Error('arc flag expected at ' + i);
    i++;
    return c === '1' ? 1 : 0;
  };
  let cmd = null;
  while (true) {
    skip();
    if (i >= n) break;
    const c = d[i];
    if (/[a-zA-Z]/.test(c)) {
      cmd = c;
      i++;
    } else if (cmd === null) {
      throw new Error('path must start with a command');
    } else if (cmd === 'M') {
      cmd = 'L'; // extra pairs after a moveto are linetos
    } else if (cmd === 'm') {
      cmd = 'l';
    }
    const lower = cmd.toLowerCase();
    const k = arity[lower];
    if (k === undefined) throw new Error('unknown command ' + cmd);
    if (k === 0) {
      out.push({ cmd: 'Z', args: [] });
      continue;
    }
    const args = [];
    for (let j = 0; j < k; j++) args.push(lower === 'a' && (j === 3 || j === 4) ? flag() : number());
    out.push({ cmd, args });
  }
  return out;
}

const fmt = (v) => {
  const r = Math.round(v * 1000) / 1000;
  return (Object.is(r, -0) ? 0 : r).toString();
};

function toWpf(d, evenOdd) {
  const parts = [evenOdd ? 'F0' : 'F1'];
  for (const seg of parsePath(d)) {
    parts.push(seg.cmd);
    for (const a of seg.args) parts.push(fmt(a));
  }
  return parts.join(' ');
}

function readIcon(file) {
  const svg = fs.readFileSync(file, 'utf8');
  const paths = [...svg.matchAll(/<path\b([^>]*)\/?>/g)];
  if (paths.length === 0) throw new Error('no path in ' + file);
  const ds = [];
  let evenOdd = false;
  for (const p of paths) {
    const attrs = p[1];
    const d = /\sd="([^"]+)"/.exec(attrs);
    if (!d) continue;
    if (/fill-rule="evenodd"/.test(attrs)) evenOdd = true;
    ds.push(d[1]);
  }
  return toWpf(ds.join(' '), evenOdd);
}

const pascal = (snake) => snake.split('_').map((w) => w.charAt(0).toUpperCase() + w.slice(1)).join('');

// ------------------------------------------------------------------ UI icons (20 px)

const ui = [
  // file and app
  'add', 'arrow_undo', 'arrow_redo', 'save', 'folder_open', 'document', 'document_add', 'document_pdf', 'arrow_export_ltr',
  'arrow_import', 'arrow_download', 'arrow_sync', 'settings', 'info', 'search', 'dismiss', 'more_horizontal', 'more_vertical',
  'chevron_down', 'chevron_up', 'chevron_left', 'chevron_right', 'checkmark', 'checkmark_circle', 'warning', 'error_circle',
  'question_circle', 'history', 'pin', 'pin_off', 'dark_theme', 'globe', 'heart', 'keyboard', 'window_new', 'home', 'open',
  'delete', 'copy', 'cut', 'clipboard_paste', 'edit', 'eye', 'eye_off', 'lock_closed', 'lock_open', 'clock', 'timer',
  'arrow_reset', 'arrow_left', 'arrow_right', 'arrow_up', 'arrow_down', 'arrow_maximize', 'arrow_minimize', 'apps',
  // insert
  'text_t', 'text_add_t', 'shapes', 'square', 'circle', 'triangle', 'line_horizontal_1', 'connector', 'chat', 'image', 'image_add',
  'icons', 'emoji', 'data_bar_vertical', 'data_pie', 'data_line', 'data_usage', 'table', 'table_insert_row', 'table_insert_column',
  'table_delete_row', 'table_delete_column', 'slide_add', 'slide_layout', 'slide_text', 'slide_multiple', 'slide_hide',
  'slide_transition', 'slide_size', 'slide_grid', 'slide_text_edit', 'slide_play', 'slide_microphone', 'square_multiple',
  // text
  'text_bold', 'text_italic', 'text_underline', 'text_align_left', 'text_align_center', 'text_align_right', 'text_align_justify',
  'text_bullet_list_ltr', 'text_number_list_ltr', 'text_line_spacing', 'text_font', 'text_font_size', 'text_color',
  'text_case_uppercase', 'text_quote', 'text_align_distributed_vertical', 'text_paragraph', 'text_header_1', 'text_clear_formatting',
  // arrange
  'align_left', 'align_center_horizontal', 'align_right', 'align_top', 'align_center_vertical', 'align_bottom',
  'align_space_evenly_horizontal', 'align_space_evenly_vertical', 'position_to_front', 'position_to_back', 'position_forward',
  'position_backward', 'group', 'group_dismiss', 'flip_horizontal', 'flip_vertical', 'arrow_rotate_clockwise', 'resize',
  'paint_brush', 'paint_bucket', 'color', 'color_fill', 'color_line', 'border_outside', 'line_thickness', 'line_style',
  'sparkle', 'wand', 'grid', 'grid_dots', 'ruler', 'layer', 'square_shadow',
  // view and show
  'zoom_in', 'zoom_out', 'zoom_fit', 'board', 'text_bullet_list_square', 'note', 'notepad', 'play', 'play_circle', 'pause',
  'stop', 'next', 'previous', 'presenter', 'pen', 'highlight', 'laser_tool', 'inking_tool', 'eraser', 'desktop', 'cursor',
  'flash', 'video', 'record', 'calendar', 'people', 'person', 'lightbulb', 'rocket', 'star', 'library',
];
const uiFilled = ['play', 'pause', 'star', 'heart', 'pin', 'lock_closed', 'checkmark_circle', 'warning', 'error_circle', 'info',
  'circle', 'square', 'presenter', 'record', 'sparkle', 'eye'];

function uiIcon(name, style) {
  for (const size of [20, 24, 16]) {
    const f = path.join(iconsDir, `${name}_${size}_${style}.svg`);
    if (fs.existsSync(f)) {
      let data = readIcon(f);
      if (size !== 20) data = scaleNote(data, size);
      return data;
    }
  }
  throw new Error('missing UI icon ' + name + ' ' + style);
}

// A 24 / 16 px icon in a 20 px slot: wrap the geometry in a scale (WPF path markup can't, so we scale the numbers).
function scaleNote(data, size) {
  const k = 20 / size;
  const tokens = data.split(' ');
  const out = [tokens[0]];
  let cmd = null;
  let idx = 0;
  for (let t = 1; t < tokens.length; t++) {
    const tok = tokens[t];
    if (/^[a-zA-Z]$/.test(tok)) {
      cmd = tok.toLowerCase();
      idx = 0;
      out.push(tok);
      continue;
    }
    const v = parseFloat(tok);
    // arc: rx ry rotation large sweep x y - only radii and the end point scale
    const scaled = cmd === 'a' ? ([0, 1, 5, 6].includes(idx % 7) ? v * k : v) : v * k;
    out.push(fmt(scaled));
    idx++;
  }
  return out.join(' ');
}

let xaml = '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"\n' +
  '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n' +
  '    <!-- Fluent UI System Icons (Copyright (c) 2020 Microsoft Corporation, MIT License), 20x20 coordinate space.\n' +
  `         Generated by tools/make-icons.mjs from @fluentui/svg-icons ${version}; do not edit by hand. -->\n`;
for (const name of [...new Set(ui)].sort()) {
  xaml += `    <Geometry x:Key="Icon.${pascal(name)}">${uiIcon(name, 'regular')}</Geometry>\n`;
}
for (const name of [...new Set(uiFilled)].sort()) {
  xaml += `    <Geometry x:Key="Icon.${pascal(name)}Filled">${uiIcon(name, 'filled')}</Geometry>\n`;
}
xaml += '</ResourceDictionary>\n';
fs.writeFileSync(path.join(root, 'src/Stagecraft/Themes/Icons.xaml'), xaml, 'utf8');
console.log('Icons.xaml: ' + (new Set(ui).size + new Set(uiFilled).size) + ' icons');

// ------------------------------------------------------------------ slide icon catalogue (24 px)

// Simple, general-purpose icons: one or two words, no state variants (off / add / dismiss / arrows / sizes ...).
const exclude = /_(off|dismiss|add|subtract|rtl|ltr|edit|error|warning|prohibited|sync|settings|search|lock|link|checkmark|question|info|star|person|clock|sparkle|arrow|down|up|left|right|90|270|\d+)(_|$)/;
const skip = new Set(['fluent', 'agents', 'approvals_app', 'learning_app', 'inprivate_account', 'incognito', 'guardian', 'midi',
  'f_stop', 'hd', 'hdr', 'gif', 'cellular_3g', 'cellular_4g', 'cellular_5g', 'dentist', 'mold', 'iot_alert', 'device_eq']);

// Extra words people search for.
const synonyms = {
  rocket: 'launch startup growth', money: 'cash dollar finance price', people: 'team users group', person: 'user profile',
  people_team: 'team group staff', building: 'office company', lightbulb: 'idea insight', target: 'goal aim focus',
  data_trending: 'growth chart up increase', data_pie: 'chart share', data_histogram: 'chart bars statistics', heart: 'love like health',
  shield: 'security protect safe', lock_closed: 'secure private', globe: 'world international web', earth: 'world planet globe',
  leaf_one: 'nature eco plant green', leaf_two: 'nature eco plant green', leaf_three: 'nature eco plant green',
  trophy: 'award win prize', ribbon: 'award badge', medal: 'award prize winner', handshake: 'deal partner agreement',
  briefcase: 'business work job', calendar: 'date schedule plan', clock: 'time hour', chat: 'message talk conversation',
  mail: 'email letter message', phone: 'mobile call', laptop: 'computer pc', desktop: 'computer monitor screen',
  cloud: 'storage online', database: 'data storage server', code: 'developer programming', bug: 'issue defect',
  cart: 'shopping buy store', gift: 'present', airplane: 'travel flight', vehicle_car: 'car transport', home: 'house',
  drink_coffee: 'coffee cafe cup', food: 'eat restaurant', star: 'favourite rating', flag: 'milestone goal',
  checkmark_circle: 'done complete ok yes', dismiss_circle: 'no cancel close', alert: 'bell notification', megaphone: 'announce marketing',
  puzzle_piece: 'solution part', diamond: 'value premium', compass_northwest: 'direction navigate explore', map: 'location directions',
  location: 'pin place map', camera: 'photo picture', video: 'film movie', mic: 'microphone audio podcast', music_note_1: 'audio song',
  book: 'read learn study', hat_graduation: 'education school learn', beaker: 'science lab experiment', brain: 'think mind ai',
  wrench: 'tool fix settings', toolbox: 'tools kit', key: 'access password', fire: 'hot trending', flash: 'fast energy power',
  speedometer: 'performance speed fast', gauge: 'performance meter', scales: 'balance law justice', gavel: 'law legal',
  document: 'file page paper', folder: 'files directory', box: 'package product', cube: 'product 3d block',
  organization: 'hierarchy structure chart', flowchart: 'process diagram', diagram: 'process flow', timeline: 'history roadmap',
  sparkle: 'magic new ai', wand: 'magic', paint_brush: 'design art', color: 'palette design', design_ideas: 'design creative',
  weather_sunny: 'sun summer day', weather_moon: 'night', mountain_trail: 'adventure outdoor hike', tree_deciduous: 'nature forest',
  savings: 'money piggy bank save', wallet: 'money pay', receipt: 'invoice bill', building_bank: 'bank finance',
  data_bar_vertical: 'chart bars', chart_multiple: 'charts analytics', arrow_trending: 'growth increase up',
  people_audience: 'audience crowd event', presenter: 'speaker talk presentation',
};

const catalogue = [];
for (const f of fs.readdirSync(iconsDir).sort()) {
  const m = /^(.+)_24_regular\.svg$/.exec(f);
  if (!m) continue;
  const key = m[1];
  if (key.split('_').length > 2 || exclude.test(key) || skip.has(key)) continue;
  try {
    catalogue.push(`${key}\t${(synonyms[key] || '').trim()}\t${readIcon(path.join(iconsDir, f))}`);
  } catch (e) {
    console.warn('skipped ' + key + ': ' + e.message);
  }
}
// A few useful ones that the filter above drops.
for (const key of ['arrow_trending_lines', 'arrow_right', 'arrow_up', 'arrow_down', 'arrow_left', 'person_add', 'star_emphasis',
  'shield_checkmark', 'checkmark_starburst', 'people_add', 'trophy', 'ribbon', 'target_arrow', 'music_note_1', 'music_note_2',
  'data_bar_vertical', 'data_bar_horizontal', 'vehicle_car', 'vehicle_bicycle', 'vehicle_truck', 'weather_sunny', 'weather_moon',
  'weather_cloudy', 'weather_rain', 'weather_snow', 'tree_deciduous', 'tree_evergreen', 'puzzle_piece', 'savings', 'wallet',
  'receipt', 'speedometer', 'scales', 'toolbox', 'wrench', 'shield', 'star', 'target', 'timeline', 'trophy']) {
  const f = path.join(iconsDir, `${key}_24_regular.svg`);
  if (fs.existsSync(f) && !catalogue.some((l) => l.startsWith(key + '\t'))) {
    catalogue.push(`${key}\t${(synonyms[key] || '').trim()}\t${readIcon(f)}`);
  }
}
catalogue.sort();
const header = `# Fluent UI System Icons (Copyright (c) 2020 Microsoft Corporation, MIT License), 24x24.\n# Generated by tools/make-icons.mjs from @fluentui/svg-icons ${version}; do not edit by hand.\n`;
fs.mkdirSync(path.join(root, 'src/Stagecraft.Core/Assets'), { recursive: true });
fs.writeFileSync(path.join(root, 'src/Stagecraft.Core/Assets/IconCatalog.txt'), header + catalogue.join('\n') + '\n', 'utf8');
console.log('IconCatalog.txt: ' + catalogue.length + ' icons');
