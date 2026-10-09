'use strict';
// Minimal synthetic DOM with real tree ownership, replacement, event dispatch,
// File blobs and FileList-like setters. Never injected/registered by extension.
const { File } = require('node:buffer');
const { createDomDriver } = require('../dom-driver.js');
class Element {
  constructor(tag, attrs = {}, text = '') { this.tagName = tag.toUpperCase(); this.attrs = attrs; this._text = text; this.children = []; this.parentElement = null; this.hidden = false; this.disabled = false; this.readOnly = false; this._files = []; this.events = []; this.writes = 0; this.handlers = {}; }
  append(node) { node.parentElement = this; node.ownerDocument = this.ownerDocument; this.children.push(node); return node; }
  remove() { if (this.parentElement) this.parentElement.children = this.parentElement.children.filter(x => x !== this); this.parentElement = null; }
  get isConnected() { let n = this; while (n.parentElement) n = n.parentElement; return n === this.ownerDocument?.body; }
  getAttribute(name) { return this.attrs[name] ?? null; }
  setAttribute(name, value) { this.attrs[name] = String(value); }
  get type() { return this.attrs.type; }
  get multiple() { return this.attrs.multiple === 'true'; }
  get innerText() { return this._text + this.children.map(x => x.innerText).join('\n'); }
  get textContent() { return this.innerText; }
  set textContent(value) { this.writes++; this._text = value; this.children.forEach(x => x.parentElement = null); this.children = []; }
  get value() { return this._text; }
  set value(value) { this.writes++; this._text = value; }
  get files() { return this._files; }
  set files(files) { this.writes++; this._files = files; }
  getClientRects() { return this.hidden ? [] : [{}]; }
  matches(selector) {
    selector = selector.trim();
    const attributes = [...selector.matchAll(/\[([\w-]+)(?:([\^]?=)"([^"]*)")?\]/g)];
    selector = selector.replace(/\[[^\]]+\]/g, '');
    const id = /#([\w-]+)/.exec(selector)?.[1], cls = /\.([\w-]+)/.exec(selector)?.[1];
    const tag = /^[a-z]+/i.exec(selector)?.[0];
    return (!tag || this.tagName === tag.toUpperCase()) && (!id || this.attrs.id === id) &&
      (!cls || (this.attrs.class ?? '').split(' ').includes(cls)) && attributes.every(([, key, op, value]) =>
        key in this.attrs && (!op || (op === '=' ? this.attrs[key] === value : this.attrs[key].startsWith(value))));
  }
  querySelectorAll(selector) {
    const found = [];
    const visit = node => { for (const child of node.children) { if (selector.split(',').some(s => child.matches(s))) found.push(child); visit(child); } };
    visit(this); return found;
  }
  closest(selector) { let n = this; while (n) { if (n.matches(selector)) return n; n = n.parentElement; } return null; }
  dispatchEvent(event) { this.events.push(event.type); this.handlers[event.type]?.(event); return true; }
}
class DataTransfer { constructor() { this.list = []; this.items = { add: file => this.list.push(file) }; } get files() { return this.list.slice(); } }
class Event { constructor(type, init) { this.type = type; Object.assign(this, init); } }
function fixture() {
  const document = { defaultView: { File, DataTransfer, Event, InputEvent: Event, HTMLTextAreaElement: Element,
    getComputedStyle: n => ({ display: n.attrs.style === 'display:none' ? 'none' : 'block', visibility: n.attrs.visibility ?? 'visible' }) } };
  document.body = new Element('body'); document.body.ownerDocument = document;
  document.querySelectorAll = selector => document.body.querySelectorAll(selector);
  const add = (tag, attrs, text, parent = document.body) => parent.append(new Element(tag, attrs, text));
  const identity = add('div', { class: 'identity', account: 'account-one', workspace: 'workspace-one', conversation: 'chat-one' });
  const form = add('form');
  const editor = add('div', { id: 'prompt-textarea', contenteditable: 'true' }, 'Original draft', form);
  const input = add('input', { type: 'file', multiple: 'true' }, '', form);
  const coverage = add('div', { class: 'coverage', complete: 'true', first: 'u1', last: 'a1' });
  function turn(role, id, text, complete = 'true') {
    const node = add('article', { class: 'turn', role, id, complete });
    add('div', { class: 'body' }, text, node); return node;
  }
  const user = turn('user', 'u1', 'Synthetic user request');
  const assistant = turn('assistant', 'a1', 'Synthetic completed answer');
  const location = { href: 'https://fixture.invalid/c/chat-one' };
  const receipts = [];
  const profile = Object.freeze({ id: 'synthetic-test-only-v1', origin: 'https://fixture.invalid',
    evidence: Object.freeze({accountSignal:'fixture-active-account',workspaceSignal:'fixture-active-workspace',conversationSignal:'fixture-chat',historySignal:'fixture-complete-history'}),
    identityVerified: true, draftVerified: true, historyVerified: true, attachmentsVerified: true, liveValidated: false,
    editor: '#prompt-textarea[contenteditable="true"]', turns: '.turn', body: '.body', files: 'input[type="file"]',
    readIdentity(doc) {
      const nodes = doc.querySelectorAll('.identity'); if (nodes.length !== 1) return null;
      return { accountId: nodes[0].attrs.account, workspaceId: nodes[0].attrs.workspace, conversationId: nodes[0].attrs.conversation };
    },
    readCoverage() { return { complete: coverage.attrs.complete === 'true', hasEarlier: false, hasLater: false, signal:'fixture-complete-history', scope: 'rendered-completed-pairs', conversationId: identity.attrs.conversation, firstId: coverage.attrs.first, lastId: coverage.attrs.last }; },
    readTurn(node) { return { role: node.attrs.role, id: node.attrs.id, complete: node.attrs.complete === 'true' }; },
    readAttachments() { return receipts.map(x => ({ ...x })); }
  });
  const driver = createDomDriver(document, location, profile);
  return { document, location, profile, driver, identity, form, editor, input, coverage, user, assistant, turn, add, receipts };
}
module.exports = { fixture, Element, DataTransfer, Event };
