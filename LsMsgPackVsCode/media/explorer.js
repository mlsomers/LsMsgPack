// The MsgPack Explorer webview: what the explorer control of MsgPackExplorer shows (the WinForms control the Visual Studio
// visualizer and the Fiddler inspector host), with the model computed by the inspector (Server/InspectorDocument.cs).
// @ts-check
(function () {
  'use strict';

  // eslint-disable-next-line no-undef
  const vscode = acquireVsCodeApi();

  const ENDIAN_CHOICES = [
    ['SwapIfCurrentSystemIsLittleEndian', 'Reorder if system is little endian (default).'],
    ['NeverSwap', 'Never reorder'],
    ['AlwaysSwap', 'Always reorder']
  ];
  const LIMITS = [500, 1000, 10000, 100000, 0];
  const BADGES = {
    nil: 'nil', bool: 'bool', int: '123', float: '1.5', bin: 'bin', str: 'abc', array: '[ ]', map: '{ }',
    entry: 'k:v', ext: 'ext', error: '!', root: 'root', other: '?'
  };
  const SEVERITY = {
    Error: ['✖', 'Error'],
    Warning: ['⚠', 'Warning'],
    Comment: ['ℹ', 'Comment'],
    ReadAbortError: ['⛔', 'Reading stopped here']
  };

  /** Kept between the webview being hidden and shown again, and when VS Code restarts. */
  const saved = vscode.getState() || {};

  const state = {
    title: '',
    description: '',
    warning: '',
    canRefresh: false,
    /** @type {Uint8Array} */
    bytes: new Uint8Array(0),
    settings: {
      continueOnError: !!saved.continueOnError,
      endian: saved.endian || 'SwapIfCurrentSystemIsLittleEndian',
      displayLimit: typeof saved.displayLimit === 'number' ? saved.displayLimit : 1000,
      objects: 'auto'
    },
    /** @type {any} */
    model: null,
    loadSeq: 0,
    searchSeq: 0,
    loading: false,
    /** @type {{ text: string, matchCase: boolean, result: any, position: number } | null} */
    search: null,
    sizes: Object.assign({ right: 0.45, props: 0.4, issues: 120, objects: 0.35, objProps: 0.4, issueDetails: 0.4 }, saved.sizes || {})
  };

  function saveState() {
    vscode.setState({
      continueOnError: state.settings.continueOnError,
      endian: state.settings.endian,
      displayLimit: state.settings.displayLimit,
      sizes: state.sizes
    });
  }

  // ---------------------------------------------------------------- DOM helpers

  /**
   * @param {string} tag
   * @param {Object<string, any>} [props]
   * @param {...(Node|string|null|undefined)} children
   * @returns {HTMLElement}
   */
  function el(tag, props, ...children) {
    const element = document.createElement(tag);
    if (props) {
      for (const key of Object.keys(props)) {
        const value = props[key];
        if (value === undefined || value === null || value === false) {
          continue;
        }
        if (key === 'className') {
          element.className = value;
        } else if (key === 'text') {
          element.textContent = value;
        } else if (key.startsWith('on')) {
          element.addEventListener(key.substring(2), value);
        } else {
          element.setAttribute(key, value === true ? '' : value);
        }
      }
    }
    for (const child of children) {
      if (child !== null && child !== undefined) {
        element.append(child);
      }
    }
    return element;
  }

  function hex(value, digits) {
    return value.toString(16).toUpperCase().padStart(digits, '0');
  }

  // ---------------------------------------------------------------- Splitters

  /**
   * A bar between two panes: dragging it changes state.sizes[key] (a fraction of the container, or pixels when absolute).
   */
  function splitter(direction, key, container, options) {
    const bar = el('div', { className: `splitter ${direction}` });
    bar.addEventListener('pointerdown', (event) => {
      event.preventDefault();
      bar.setPointerCapture(event.pointerId);
      bar.classList.add('dragging');
      const rect = container().getBoundingClientRect();
      const move = (/** @type {PointerEvent} */ e) => {
        const total = direction === 'v' ? rect.width : rect.height;
        let position = direction === 'v' ? e.clientX - rect.left : e.clientY - rect.top;
        position = Math.max(40, Math.min(total - 40, position));
        // The size of the pane after (right of or below) the bar, unless it is the first one
        const size = options.after ? total - position : position;
        state.sizes[key] = options.absolute ? size : size / total;
        layout();
      };
      const up = () => {
        bar.classList.remove('dragging');
        bar.removeEventListener('pointermove', move);
        bar.removeEventListener('pointerup', up);
        saveState();
      };
      bar.addEventListener('pointermove', move);
      bar.addEventListener('pointerup', up);
    });
    return bar;
  }

  // ---------------------------------------------------------------- Virtual tree

  /**
   * A tree that only creates the visible rows (data can have many items).
   * Nodes: { id, parent, kind, text, title?, role?, classes? }, children in the order of the list.
   */
  class VirtualTree {
    constructor(onSelect) {
      this.onSelect = onSelect;
      this.element = el('div', { className: 'tree', tabindex: '0', role: 'tree' });
      this.rowsElement = el('div', { className: 'rows' });
      this.spacer = el('div');
      this.element.append(this.spacer, this.rowsElement);
      this.nodes = [];
      this.children = [];
      this.collapsed = new Set();
      this.visible = [];
      this.selected = -1;
      this.emptyText = '';
      this.element.addEventListener('scroll', () => this.render());
      this.element.addEventListener('keydown', (e) => this.key(e));
      this.element.addEventListener('click', (e) => {
        const row = /** @type {HTMLElement} */ (e.target).closest('.row');
        if (!row) {
          return;
        }
        const id = Number(row.getAttribute('data-id'));
        if (/** @type {HTMLElement} */ (e.target).classList.contains('twisty')) {
          this.toggle(id);
        } else {
          this.select(id, true);
        }
      });
      this.element.addEventListener('dblclick', (e) => {
        const row = /** @type {HTMLElement} */ (e.target).closest('.row');
        if (row && !(/** @type {HTMLElement} */ (e.target).classList.contains('twisty'))) {
          this.toggle(Number(row.getAttribute('data-id')));
        }
      });
      new ResizeObserver(() => this.render()).observe(this.element);
    }

    setNodes(nodes, emptyText) {
      this.nodes = nodes;
      this.emptyText = emptyText || '';
      this.children = nodes.map(() => []);
      this.roots = [];
      for (const node of nodes) {
        if (node.parent >= 0) {
          this.children[node.parent].push(node.id);
        } else {
          this.roots.push(node.id);
        }
      }
      this.collapsed = new Set(); // expanded like the explorer (ExpandAll)
      this.selected = -1;
      this.element.scrollTop = 0;
      this.refreshVisible();
    }

    refreshVisible() {
      const visible = [];
      const depth = [];
      const add = (id, level) => {
        visible.push(id);
        depth[id] = level;
        if (!this.collapsed.has(id)) {
          for (const child of this.children[id]) {
            add(child, level + 1);
          }
        }
      };
      // Deep data: iterative would be safer, but the depth is limited (MaxDepth 256)
      for (const root of this.roots || []) {
        add(root, 0);
      }
      this.visible = visible;
      this.depth = depth;
      this.spacer.style.height = `${visible.length * rowHeight()}px`;
      this.render();
    }

    render() {
      const height = rowHeight();
      const top = this.element.scrollTop;
      const first = Math.max(0, Math.floor(top / height) - 5);
      const last = Math.min(this.visible.length, Math.ceil((top + this.element.clientHeight) / height) + 5);
      this.rowsElement.style.top = `${first * height}px`;
      this.rowsElement.replaceChildren();
      if (this.visible.length === 0 && this.emptyText) {
        this.rowsElement.append(el('div', { className: 'empty', text: this.emptyText }));
        return;
      }
      for (let index = first; index < last; index++) {
        const id = this.visible[index];
        const node = this.nodes[id];
        const hasChildren = this.children[id].length > 0;
        const row = el('div', {
          className: `row${id === this.selected ? ' selected' : ''}${node.classes ? ' ' + node.classes : ''}`,
          'data-id': String(id),
          role: 'treeitem',
          title: node.title || node.text,
          'aria-expanded': hasChildren ? String(!this.collapsed.has(id)) : undefined,
          'aria-selected': String(id === this.selected)
        },
          el('span', { className: 'twisty', style: `margin-left:${this.depth[id] * 14}px` }, hasChildren ? (this.collapsed.has(id) ? '▶' : '▼') : ''),
          el('span', { className: `badge ${node.kind}` }, BADGES[node.kind] || node.kind || ''),
          node.role ? el('span', { className: 'role', text: node.role === 'key' ? 'key' : 'value' }) : null,
          el('span', { className: 'text', text: node.text }));
        this.rowsElement.append(row);
      }
    }

    toggle(id) {
      if (this.children[id].length === 0) {
        return;
      }
      if (this.collapsed.has(id)) {
        this.collapsed.delete(id);
      } else {
        this.collapsed.add(id);
      }
      this.refreshVisible();
    }

    /**
     * Selects the node (expands its parents and scrolls to it), raises onSelect unless it is called with notify false.
     */
    select(id, notify) {
      if (id < 0 || id >= this.nodes.length) {
        this.selected = -1;
        this.render();
        return;
      }
      let changed = false;
      for (let parent = this.nodes[id].parent; parent >= 0; parent = this.nodes[parent].parent) {
        if (this.collapsed.delete(parent)) {
          changed = true;
        }
      }
      this.selected = id;
      if (changed) {
        this.refreshVisible();
      }
      this.scrollTo(id);
      this.render();
      if (notify) {
        this.onSelect(id);
      }
    }

    scrollTo(id) {
      const index = this.visible.indexOf(id);
      if (index < 0) {
        return;
      }
      const height = rowHeight();
      const top = index * height;
      if (top < this.element.scrollTop) {
        this.element.scrollTop = top;
      } else if (top + height > this.element.scrollTop + this.element.clientHeight) {
        this.element.scrollTop = top + height - this.element.clientHeight;
      }
    }

    key(/** @type {KeyboardEvent} */ e) {
      if (this.visible.length === 0) {
        return;
      }
      const index = this.visible.indexOf(this.selected);
      let target = -1;
      switch (e.key) {
        case 'ArrowDown': target = this.visible[Math.min(this.visible.length - 1, index + 1)]; break;
        case 'ArrowUp': target = this.visible[Math.max(0, index - 1)]; break;
        case 'Home': target = this.visible[0]; break;
        case 'End': target = this.visible[this.visible.length - 1]; break;
        case 'PageDown': target = this.visible[Math.min(this.visible.length - 1, index + Math.floor(this.element.clientHeight / rowHeight()))]; break;
        case 'PageUp': target = this.visible[Math.max(0, index - Math.floor(this.element.clientHeight / rowHeight()))]; break;
        case 'ArrowRight':
          if (this.selected >= 0 && this.collapsed.has(this.selected)) {
            this.toggle(this.selected);
          } else if (this.selected >= 0 && this.children[this.selected].length > 0) {
            target = this.children[this.selected][0];
          }
          break;
        case 'ArrowLeft':
          if (this.selected >= 0 && this.children[this.selected].length > 0 && !this.collapsed.has(this.selected)) {
            this.toggle(this.selected);
          } else if (this.selected >= 0 && this.nodes[this.selected].parent >= 0) {
            target = this.nodes[this.selected].parent;
          }
          break;
        default:
          return;
      }
      e.preventDefault();
      if (target !== undefined && target >= 0 && target !== this.selected) {
        this.select(target, true);
      }
    }
  }

  let cachedRowHeight = 0;
  function rowHeight() {
    if (!cachedRowHeight) {
      cachedRowHeight = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--row-height')) || 20;
    }
    return cachedRowHeight;
  }

  // ---------------------------------------------------------------- Hex view

  /**
   * 16 bytes per row: the type byte of each item in red, the bytes holding a length in blue, bytes after the data in gray,
   * the selected item (or object) on a green background. Clicking a byte selects the item it belongs to.
   */
  class HexView {
    constructor(onByte) {
      this.onByte = onByte;
      this.element = el('div', { className: 'hex', tabindex: '0' });
      this.rowsElement = el('div', { className: 'rows' });
      this.spacer = el('div');
      this.element.append(this.spacer, this.rowsElement);
      this.bytes = new Uint8Array(0);
      this.classes = new Uint8Array(0);
      this.rangeStart = -1;
      this.rangeEnd = -1;
      this.element.addEventListener('scroll', () => this.render());
      this.element.addEventListener('click', (e) => {
        const target = /** @type {HTMLElement} */ (e.target);
        const offset = target.getAttribute('data-o');
        if (offset !== null) {
          this.onByte(Number(offset));
        }
      });
      new ResizeObserver(() => this.render()).observe(this.element);
    }

    setData(bytes, items) {
      this.bytes = bytes;
      // 0 data, 1 type byte, 2 length bytes, 3 not part of the data (after it, or the whole data when it could not be read)
      const classes = new Uint8Array(bytes.length);
      if (items && items.length > 0) {
        const root = items[0];
        classes.fill(3);
        classes.fill(0, Math.max(0, root.offset), Math.min(bytes.length, root.offset + Math.max(root.length, 0)));
        for (const item of items) {
          let offset = item.offset;
          if (item.typeBytes > 0 && offset >= 0 && offset < bytes.length) {
            classes[offset++] = 1;
            for (let t = 0; t < item.lengthBytes && offset < bytes.length; t++) {
              classes[offset++] = 2;
            }
          }
        }
      }
      this.classes = classes;
      this.rangeStart = this.rangeEnd = -1;
      this.spacer.style.height = `${Math.ceil(bytes.length / 16) * rowHeight()}px`;
      this.element.scrollTop = 0;
      this.render();
    }

    setRange(start, end, scroll) {
      this.rangeStart = start;
      this.rangeEnd = end;
      if (scroll && start >= 0) {
        const height = rowHeight();
        const top = Math.floor(start / 16) * height;
        const bottom = Math.ceil(Math.max(end, start + 1) / 16) * height;
        if (top < this.element.scrollTop || bottom > this.element.scrollTop + this.element.clientHeight) {
          this.element.scrollTop = Math.max(0, top - height);
        }
      }
      this.render();
    }

    render() {
      const height = rowHeight();
      const rows = Math.ceil(this.bytes.length / 16);
      const top = this.element.scrollTop;
      const first = Math.max(0, Math.floor(top / height) - 3);
      const last = Math.min(rows, Math.ceil((top + this.element.clientHeight) / height) + 3);
      const digits = Math.max(4, hex(Math.max(0, this.bytes.length - 1), 1).length);
      const names = ['', ' t', ' l', ' x'];
      this.rowsElement.style.top = `${first * height}px`;
      const html = [];
      for (let row = first; row < last; row++) {
        const start = row * 16;
        let line = `<div class="row"><span class="off">${hex(start, digits)}</span>`;
        let ascii = '<span class="ascii">';
        for (let column = 0; column < 16; column++) {
          const offset = start + column;
          if (offset >= this.bytes.length) {
            line += `<span class="b none${column === 8 ? ' gap' : ''}"> </span>`; // keeps the text column in place
            continue;
          }
          const value = this.bytes[offset];
          const inRange = offset >= this.rangeStart && offset < this.rangeEnd ? ' r' : '';
          line += `<span class="b${column === 8 ? ' gap' : ''}${names[this.classes[offset]]}${inRange}" data-o="${offset}">${hex(value, 2)}</span>`;
          const ch = value >= 32 && value < 127 ? String.fromCharCode(value) : '.';
          ascii += `<span class="a${inRange}" data-o="${offset}">${ch === '<' ? '&lt;' : ch === '&' ? '&amp;' : ch === '>' ? '&gt;' : ch}</span>`;
        }
        html.push(line + ascii + '</span></div>');
      }
      this.rowsElement.innerHTML = html.join('');
    }
  }

  // ---------------------------------------------------------------- Property grid

  /**
   * Read-only properties grouped by category (as a categorized property grid), with the description of the selected one below.
   */
  class PropertyGrid {
    constructor() {
      this.element = el('div', { className: 'pane grow' });
      this.table = el('div', { className: 'props' });
      this.help = el('div', { className: 'help' });
      this.element.append(this.table, this.help);
    }

    show(props, emptyText) {
      this.help.replaceChildren();
      if (!props || props.length === 0) {
        this.table.replaceChildren(el('div', { className: 'empty', text: emptyText || '' }));
        return;
      }
      const categories = new Map();
      for (const prop of props) {
        const category = prop.category || 'Misc';
        if (!categories.has(category)) {
          categories.set(category, []);
        }
        categories.get(category).push(prop);
      }
      const tbody = el('tbody');
      for (const category of Array.from(categories.keys()).sort()) {
        tbody.append(el('tr', { className: 'category' }, el('td', { colspan: '2', text: category })));
        for (const prop of categories.get(category)) {
          const row = el('tr', { className: 'prop', title: prop.value },
            el('td', { className: 'name', text: prop.name }),
            el('td', { className: 'value', text: prop.value }));
          row.addEventListener('click', () => {
            for (const selected of tbody.querySelectorAll('tr.selected')) {
              selected.classList.remove('selected');
            }
            row.classList.add('selected');
            this.help.replaceChildren(el('b', { text: prop.name }), prop.description || '', prop.value && prop.value.length > 60 ? `\n\n${prop.value}` : '');
          });
          tbody.append(row);
        }
      }
      this.table.replaceChildren(el('table', {}, tbody));
    }
  }

  // ---------------------------------------------------------------- The page

  const ui = {};

  function build() {
    const app = /** @type {HTMLElement} */ (document.getElementById('app'));

    ui.refresh = el('button', { title: 'Read the bytes again (from the debugger while the program is paused, or from the file)', onclick: () => post({ type: 'refresh' }) }, 'Refresh');
    ui.save = el('button', { title: 'Save the bytes as a file', onclick: () => post({ type: 'save' }) }, 'Save...');
    ui.copyHex = el('button', { title: 'Copy the bytes to the clipboard as hex', onclick: () => post({ type: 'copy', format: 'hex' }) }, 'Copy hex');
    ui.copyBase64 = el('button', { title: 'Copy the bytes to the clipboard as base64', onclick: () => post({ type: 'copy', format: 'base64' }) }, 'Copy base64');
    ui.ignoreErrors = el('button', {
      className: 'toggle', 'aria-pressed': 'false',
      title: 'Enable this to get a "best effort" view of contents after an error. Note that the items after an error are not reliable (shown in gray).',
      onclick: () => {
        state.settings.continueOnError = !state.settings.continueOnError;
        saveState();
        load();
      }
    }, 'Ignore errors');
    ui.objects = el('button', {
      className: 'toggle', 'aria-pressed': 'false',
      title: 'Show the objects the data was written from (switched on when the data starts with an indexed schema).',
      onclick: () => {
        state.settings.objects = ui.objects.getAttribute('aria-pressed') === 'true' ? 'hide' : 'show';
        load();
      }
    }, 'Objects');

    ui.limit = el('select', { title: 'More items take longer to process and it may seem like the application freezes for a while' });
    ui.limit.addEventListener('change', () => {
      state.settings.displayLimit = Number(/** @type {HTMLSelectElement} */ (ui.limit).value);
      saveState();
      load();
    });
    ui.endian = el('select', { title: 'Override specification (for debugging purposes). MsgPack is big-endian: on little-endian systems the bytes of numbers are reordered.' });
    for (const [value, text] of ENDIAN_CHOICES) {
      ui.endian.append(el('option', { value }, text));
    }
    ui.endian.addEventListener('change', () => {
      state.settings.endian = /** @type {HTMLSelectElement} */ (ui.endian).value;
      saveState();
      load();
    });

    ui.searchText = el('input', {
      type: 'search', placeholder: 'Search',
      title: 'Search strings containing the text, and values it converts to (numbers, true/false, null, Guids, dates and times). Enter: next, Shift+Enter: previous.'
    });
    ui.searchText.addEventListener('input', resetSearch);
    ui.searchText.addEventListener('keydown', (/** @type {KeyboardEvent} */ e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        if (e.shiftKey) {
          if (!ui.searchPrev.disabled) {
            searchStep(-1);
          }
        } else if (!ui.searchNext.disabled) {
          searchStep(1);
        }
      } else if (e.key === 'Escape') {
        /** @type {HTMLInputElement} */ (ui.searchText).value = '';
        resetSearch();
      }
    });
    ui.matchCase = el('button', {
      className: 'toggle', 'aria-pressed': 'false', title: 'Match case (of strings containing the text)',
      onclick: () => {
        ui.matchCase.setAttribute('aria-pressed', String(ui.matchCase.getAttribute('aria-pressed') !== 'true'));
        resetSearch();
      }
    }, 'Aa');
    ui.searchPrev = el('button', { title: 'Previous (Shift+Enter)', onclick: () => searchStep(-1) }, '<');
    ui.searchNext = el('button', { title: 'Next (Enter)', onclick: () => searchStep(1) }, '>');
    ui.searchCount = el('span', { className: 'search-count' }, '0/0');

    const toolbar = el('div', { className: 'toolbar', role: 'toolbar' },
      ui.refresh, ui.save, ui.copyHex, ui.copyBase64,
      el('span', { className: 'sep' }),
      ui.ignoreErrors, ui.objects,
      el('span', { className: 'sep' }),
      el('label', {}, 'Limit: '), ui.limit,
      el('label', {}, 'Endian: '), ui.endian,
      el('span', { className: 'sep' }),
      ui.searchText, ui.matchCase, ui.searchPrev, ui.searchNext, ui.searchCount,
      el('span', { className: 'spacer' }),
      el('button', { title: 'How to use the MsgPack Explorer', onclick: () => post({ type: 'help' }) }, 'Help'),
      el('button', { title: 'Versions and license', onclick: () => post({ type: 'about' }) }, 'About'));

    ui.banner = el('div', { className: 'banner', hidden: true });

    // The MsgPack tree with the validation issues below it
    ui.tree = new VirtualTree(onItemSelected);
    ui.issues = el('div', { className: 'issues', tabindex: '0' });
    ui.issueDetails = el('div', { className: 'issue-details', hidden: true });
    ui.issueArea = el('div', { className: 'issue-area' }, ui.issues,
      ui.issueDetailsSplitter = splitter('v', 'issueDetails', () => ui.issueArea, { after: true }), ui.issueDetails);
    ui.left = el('div', { className: 'pane grow' },
      el('div', { className: 'caption', text: 'MsgPack items' }),
      ui.tree.element,
      splitter('h', 'issues', () => ui.left, { after: true, absolute: true }),
      ui.issueArea);

    // The properties of the selected item above the hex view
    ui.props = new PropertyGrid();
    ui.hex = new HexView(onByteClicked);
    ui.propsPane = el('div', { className: 'pane' }, el('div', { className: 'caption', text: 'Properties' }), ui.props.element);
    ui.right = el('div', { className: 'pane' },
      ui.propsPane,
      splitter('h', 'props', () => ui.right, {}),
      el('div', { className: 'caption', text: 'Bytes' }),
      ui.hex.element);

    ui.main = el('div', { className: 'main' }, ui.left, splitter('v', 'right', () => ui.main, { after: true }), ui.right);

    // The objects (and the properties of the selected one) below
    ui.objectTree = new VirtualTree(onObjectSelected);
    ui.objectProps = new PropertyGrid();
    ui.objectPropsPane = el('div', { className: 'pane' }, el('div', { className: 'caption', text: 'Object properties' }), ui.objectProps.element);
    ui.objectsPane = el('div', { className: 'objects', hidden: true },
      el('div', { className: 'pane grow' }, el('div', { className: 'caption', text: 'Objects' }), ui.objectTree.element),
      splitter('v', 'objProps', () => ui.objectsPane, { after: true }),
      ui.objectPropsPane);
    ui.objectsSplitter = splitter('h', 'objects', () => ui.body, { after: true });
    ui.objectsSplitter.hidden = true;

    ui.status = el('div', { className: 'statusbar' },
      ui.statusOffset = el('span', {}, 'Offset: 0 (0x0)'),
      ui.statusLength = el('span', {}, ''),
      ui.statusSource = el('span', { className: 'source' }, ''));

    ui.body = el('div', { className: 'pane grow' }, ui.main, ui.objectsSplitter, ui.objectsPane);
    app.append(toolbar, ui.banner, ui.body, ui.status);
    layout();
    new ResizeObserver(layout).observe(app);
  }

  function layout() {
    const s = state.sizes;
    ui.right.style.width = `${Math.round(s.right * 100)}%`;
    ui.propsPane.style.height = `${Math.round(s.props * 100)}%`;
    ui.issueArea.style.height = `${Math.round(s.issues)}px`;
    ui.issueDetails.style.width = `${Math.round(s.issueDetails * 100)}%`;
    ui.objectsPane.style.height = `${Math.round(s.objects * 100)}%`;
    ui.objectPropsPane.style.width = `${Math.round(s.objProps * 100)}%`;
  }

  function fillLimits() {
    const select = /** @type {HTMLSelectElement} */ (ui.limit);
    const limits = LIMITS.slice();
    if (!limits.includes(state.settings.displayLimit)) {
      limits.splice(limits.length - 1, 0, state.settings.displayLimit);
    }
    select.replaceChildren(...limits.map((limit) => el('option', { value: String(limit) }, limit === 0 ? 'All (no limit)' : String(limit))));
    select.value = String(state.settings.displayLimit);
  }

  function post(message) {
    vscode.postMessage(message);
  }

  function showBanner(text, isError) {
    ui.banner.hidden = !text;
    ui.banner.textContent = text || '';
    ui.banner.classList.toggle('error', !!isError);
  }

  // ---------------------------------------------------------------- Loading

  function load() {
    state.loading = true;
    state.loadSeq++;
    resetSearch();
    ui.statusLength.textContent = `${state.bytes.length} bytes, reading...`;
    post({ type: 'load', seq: state.loadSeq, settings: state.settings });
  }

  function showModel(model) {
    state.model = model;
    state.loading = false;
    const items = model.items || [];

    ui.ignoreErrors.setAttribute('aria-pressed', String(state.settings.continueOnError));
    /** @type {HTMLSelectElement} */ (ui.endian).value = state.settings.endian;
    fillLimits();

    // The MsgPack tree
    const nodes = items.map((item) => ({
      id: item.id,
      parent: item.parent,
      kind: item.kind,
      text: item.text,
      role: item.role,
      classes: item.guess ? 'guess' : item.schema ? 'schema' : '',
      title: `${item.text}\nOffset ${item.offset} (0x${hex(item.offset, 1)}), ${item.length} bytes${item.guess ? '\nAfter an error: may not be read correctly.' : ''}${item.schema ? '\nPart of the indexed schema.' : ''}`
    }));
    if (model.truncated && nodes.length > 0) {
      nodes.push({ id: nodes.length, parent: 0, kind: 'limit', text: `Limit of ${model.displayLimit} displayed items reached...`, classes: 'limit' });
    }
    ui.tree.setNodes(nodes, model.error ? `The data could not be read:\n${model.error}\n\nSwitch on "Ignore errors" to see what could be read.` : state.bytes.length === 0 ? 'No data.' : '');
    if (model.error) {
      showBanner(`The data could not be read: ${model.error}`, true);
    } else {
      showBanner(state.warning, false);
    }

    ui.hex.setData(state.bytes, items);
    showIssues(model.issues || []);

    // The objects: shown when asked, or when the data has a schema (then the button can still hide them)
    const objects = model.objects;
    ui.objects.setAttribute('aria-pressed', String(!!objects));
    ui.objectsPane.hidden = !objects;
    ui.objectsSplitter.hidden = !objects;
    if (objects) {
      const objectNodes = objects.nodes.map((node) => ({
        id: node.id,
        parent: node.parent,
        kind: node.kind,
        text: node.text,
        classes: node.error ? 'err' : node.guess ? 'guess' : '',
        title: node.error || (node.guess ? `${node.text}\nThe type is inferred from the shape of the data (no type id).` : node.text)
      }));
      if (objects.truncated && objectNodes.length > 0) {
        objectNodes.push({ id: objectNodes.length, parent: 0, kind: 'limit', text: `Limit of ${model.displayLimit} displayed items reached...`, classes: 'limit' });
      }
      ui.objectTree.setNodes(objectNodes, 'No objects.');
      if (objectNodes.length > 0) {
        ui.objectTree.select(0, false);
        ui.objectProps.show(objects.nodes[0].props);
      }
    } else {
      ui.objectTree.setNodes([], '');
      ui.objectProps.show([], '');
    }

    ui.statusLength.textContent = `${state.bytes.length} bytes, ${items.length} items${model.truncated ? ' shown' : ''}`;
    if (items.length > 0) {
      ui.tree.select(0, true);
    } else {
      onItemSelected(-1);
    }
  }

  // ---------------------------------------------------------------- Selection

  function onItemSelected(id) {
    const items = state.model ? state.model.items || [] : [];
    const item = id >= 0 && id < items.length ? items[id] : null;
    if (!item) {
      ui.props.show([], id >= 0 ? 'More items are not shown, choose a higher limit.' : '');
      ui.hex.setRange(-1, -1, false);
      ui.statusOffset.textContent = 'Offset: 0 (0x0)';
      selectIssuesOf(-1);
      return;
    }
    ui.props.show(item.props);
    ui.statusOffset.textContent = `Offset: ${item.offset} (0x${hex(item.offset, 1)})`;
    ui.hex.setRange(item.offset, item.offset + item.length, true);
    selectIssuesOf(id);
    selectObjectFor(id);
  }

  let syncing = false;

  /**
   * The object the item belongs to: its own, or the one of the closest container it is in (the root for the schema).
   */
  function selectObjectFor(id) {
    const objects = state.model && state.model.objects;
    if (syncing || !objects || objects.nodes.length === 0) {
      return;
    }
    const items = state.model.items;
    let target = -1;
    for (let current = id; current >= 0 && target < 0; current = items[current].parent) {
      target = objects.itemObjects[current];
    }
    if (target < 0) {
      target = 0;
    }
    ui.objectTree.select(target, false);
    ui.objectProps.show(objects.nodes[target].props);
  }

  function onObjectSelected(id) {
    const objects = state.model && state.model.objects;
    const node = objects && id < objects.nodes.length ? objects.nodes[id] : null;
    if (!node) {
      ui.objectProps.show([], 'More objects are not shown, choose a higher limit.');
      return;
    }
    ui.objectProps.show(node.props);
    // Select the first item of the object in the MsgPack tree, and all of its bytes in the hex view
    syncing = true;
    try {
      if (node.item >= 0) {
        ui.tree.select(node.item, true);
      }
      if (node.start >= 0) {
        ui.hex.setRange(node.start, node.end, true);
      }
    } finally {
      syncing = false;
    }
  }

  /**
   * The item a byte belongs to: the last item in the order of the tree that starts at or before it (the innermost).
   */
  function onByteClicked(offset) {
    const items = state.model ? state.model.items || [] : [];
    for (let t = items.length - 1; t >= 0; t--) {
      if (items[t].offset <= offset && (items[t].offset + items[t].length > offset || t === 0)) {
        ui.tree.select(t, true);
        return;
      }
    }
  }

  // ---------------------------------------------------------------- Validation issues

  function showIssues(issues) {
    ui.issueDetails.hidden = true;
    ui.issueDetailsSplitter.hidden = true;
    if (issues.length === 0) {
      ui.issues.replaceChildren(el('div', { className: 'empty', text: state.model && state.model.items && state.model.items.length > 0 ? 'No validation issues.' : '' }));
      return;
    }
    const items = state.model.items;
    const tbody = el('tbody');
    issues.forEach((issue, index) => {
      const severity = SEVERITY[issue.severity] || ['•', issue.severity];
      const row = el('tr', { className: 'issue', 'data-item': String(issue.item), 'data-index': String(index), title: issue.message },
        el('td', { className: `sev sev-${issue.severity}`, title: severity[1] }, severity[0]),
        el('td', { className: 'bytes', text: String(issue.bytes) }),
        el('td', { className: 'msg' }, el('span', { className: `badge ${items[issue.item] ? items[issue.item].kind : 'other'}` }, BADGES[items[issue.item] ? items[issue.item].kind : 'other']), issue.message));
      row.addEventListener('click', () => {
        for (const selected of tbody.querySelectorAll('tr.selected')) {
          selected.classList.remove('selected');
        }
        row.classList.add('selected');
        ui.issueDetails.hidden = false;
        ui.issueDetailsSplitter.hidden = false;
        ui.issueDetails.textContent = issue.message;
        ui.tree.select(issue.item, true);
        row.classList.add('selected');
      });
      tbody.append(row);
    });
    ui.issues.replaceChildren(el('table', {},
      el('thead', {}, el('tr', {}, el('th', {}, ''), el('th', {}, 'Bytes'), el('th', {}, 'Description'))),
      tbody));
  }

  function selectIssuesOf(itemId) {
    for (const row of ui.issues.querySelectorAll('tr.issue')) {
      row.classList.toggle('selected', Number(row.getAttribute('data-item')) === itemId);
    }
  }

  // ---------------------------------------------------------------- Search

  function resetSearch() {
    state.search = null;
    const hasText = /** @type {HTMLInputElement} */ (ui.searchText).value.length > 0;
    ui.searchPrev.disabled = !hasText;
    ui.searchNext.disabled = !hasText;
    ui.searchCount.textContent = '0/0';
    ui.searchCount.title = '';
  }

  /**
   * The first step after a change searches the whole data and goes to the first item found, the next ones move through the items found.
   */
  function searchStep(step) {
    if (state.loading) {
      return;
    }
    if (!state.search) {
      const text = /** @type {HTMLInputElement} */ (ui.searchText).value;
      const matchCase = ui.matchCase.getAttribute('aria-pressed') === 'true';
      state.search = { text, matchCase, result: null, position: 0 };
      state.searchSeq++;
      ui.searchCount.textContent = '...';
      ui.searchPrev.disabled = true;
      ui.searchNext.disabled = true;
      post({ type: 'search', seq: state.searchSeq, text, matchCase, settings: state.settings });
      return;
    }
    if (!state.search.result) {
      return; // still searching
    }
    state.search.position += step;
    showSearchPosition();
  }

  function showSearchPosition() {
    const search = state.search;
    if (!search || !search.result) {
      return;
    }
    const displayed = search.result.displayed;
    const count = displayed.length;
    const total = search.result.total;
    search.position = Math.max(0, Math.min(search.position, count - 1));

    // Items beyond the display limit are not in the tree, so they are counted but cannot be selected
    let text = `${count === 0 ? 0 : search.position + 1}/${count}`;
    if (total > count) {
      text += `/${total}`;
    }
    ui.searchCount.textContent = text;
    ui.searchCount.title = total > count ? 'Position / found within the display limit / found in all the data.' : 'Position / found.';
    ui.searchPrev.disabled = search.position <= 0;
    ui.searchNext.disabled = search.position >= count - 1;
    if (count > 0) {
      ui.tree.select(displayed[search.position], true);
    }
  }

  // ---------------------------------------------------------------- Messages from the extension

  window.addEventListener('message', (event) => {
    const message = event.data;
    switch (message.type) {
      case 'init': {
        state.title = message.title;
        state.description = message.description;
        state.warning = message.warning || '';
        state.canRefresh = message.canRefresh;
        const binary = atob(message.data || '');
        const bytes = new Uint8Array(binary.length);
        for (let t = 0; t < binary.length; t++) {
          bytes[t] = binary.charCodeAt(t);
        }
        state.bytes = bytes;
        if (typeof saved.displayLimit !== 'number' && typeof message.displayLimit === 'number') {
          state.settings.displayLimit = message.displayLimit;
        }
        // New data: the objects are shown when it has a schema
        state.settings.objects = 'auto';
        ui.refresh.disabled = !state.canRefresh;
        ui.statusSource.textContent = state.description;
        ui.statusSource.title = state.description;
        document.title = `MsgPack: ${state.title}`;
        load();
        break;
      }
      case 'model':
        if (message.seq === state.loadSeq) {
          showModel(message.result);
        }
        break;
      case 'searchResult':
        if (state.search && message.seq === state.searchSeq) {
          state.search.result = message.result;
          state.search.position = 0;
          showSearchPosition();
        }
        break;
      case 'error':
        if (message.request === 'load' && message.seq === state.loadSeq) {
          state.loading = false;
          showModel({ items: [], issues: [], error: message.message });
        } else if (message.request === 'search' && state.search && message.seq === state.searchSeq) {
          state.search = null;
          ui.searchCount.textContent = 'error';
          ui.searchCount.title = message.message;
          ui.searchPrev.disabled = false;
          ui.searchNext.disabled = false;
        } else {
          showBanner(message.message, true);
        }
        break;
    }
  });

  build();
  fillLimits();
  resetSearch();
  post({ type: 'ready' });
})();
