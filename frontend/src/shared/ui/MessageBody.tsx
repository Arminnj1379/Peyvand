import { useRef, useState } from 'react';
import { Icon } from './Icon';

/**
 * Chat message body renderer. Plain text stays plain text (dir=auto for
 * bidi); fenced ``` blocks become a code panel with language label and a
 * one-click copy button; single backticks become inline code. Everything is
 * rendered as React strings — no HTML injection surface.
 */

function CodeBlock({ code, lang }: { code: string; lang: string }) {
  const [copied, setCopied] = useState(false);
  const timer = useRef<number | null>(null);

  async function copy(): Promise<void> {
    const done = () => {
      setCopied(true);
      if (timer.current) window.clearTimeout(timer.current);
      timer.current = window.setTimeout(() => setCopied(false), 1500);
    };
    try {
      await navigator.clipboard.writeText(code);
      done();
    } catch {
      // Non-secure contexts (plain http on LAN): legacy execCommand path.
      try {
        const ta = document.createElement('textarea');
        ta.value = code;
        ta.style.position = 'fixed';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.select();
        document.execCommand('copy');
        ta.remove();
        done();
      } catch {
        // Clipboard unavailable: user can still select manually.
      }
    }
  }

  return (
    <span className="code-block" dir="ltr">
      <span className="code-head">
        <span className="code-lang">{lang || 'code'}</span>
        <button
          type="button"
          className="copy-btn"
          onClick={() => void copy()}
          title={copied ? 'Copied!' : 'Copy code'}
          aria-label={copied ? 'Copied!' : 'Copy code'}
        >
          <Icon name={copied ? 'check' : 'copy'} size={14} />
          {copied ? 'Copied' : 'Copy'}
        </button>
      </span>
      <pre className="code-pre">
        <code>{code.replace(/\n$/, '')}</code>
      </pre>
    </span>
  );
}

/** Inline `code` + line breaks within a plain-text segment. */
function renderInline(text: string, keyPrefix: string): React.ReactNode[] {
  const parts = text.split(/(`[^`\n]+`)/g);
  const out: React.ReactNode[] = [];
  let li = 0;
  for (let i = 0; i < parts.length; i++) {
    const part = parts[i];
    if (part.startsWith('`') && part.endsWith('`') && part.length >= 2) {
      out.push(
        <code key={`${keyPrefix}-c${i}`} className="inline-code" dir="ltr">
          {part.slice(1, -1)}
        </code>,
      );
    } else {
      const lines = part.split('\n');
      for (let j = 0; j < lines.length; j++) {
        if (j > 0) out.push(<br key={`${keyPrefix}-b${li++}`} />);
        if (lines[j] !== '') out.push(<span key={`${keyPrefix}-t${li++}`}>{lines[j]}</span>);
      }
    }
  }
  return out;
}

export function MessageBody({ body }: { body: string }) {
  // ```lang\ncode\n``` — unclosed fence runs to end of message.
  const tokens = body.split(/```(\w*)\n?([\s\S]*?)(```|$)/g);
  if (tokens.length === 1) {
    return <>{renderInline(body, 't')}</>;
  }
  const out: React.ReactNode[] = [];
  // Split shape: [text, lang, code, fence, text, lang, code, fence, ... text].
  for (let i = 0; i < tokens.length; i += 4) {
    const text = tokens[i];
    if (text) out.push(<span key={`t${i}`}>{renderInline(text, `t${i}`)}</span>);
    if (i + 2 < tokens.length && (tokens[i + 1] !== undefined || tokens[i + 2])) {
      const lang = (tokens[i + 1] ?? '').trim().toLowerCase();
      const code = tokens[i + 2] ?? '';
      if (code !== '' || lang !== '') {
        out.push(<CodeBlock key={`c${i}`} code={code} lang={lang} />);
      }
    }
  }
  return <>{out}</>;
}
