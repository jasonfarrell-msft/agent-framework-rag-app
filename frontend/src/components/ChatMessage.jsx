import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeRaw from 'rehype-raw';

export default function ChatMessage({ role, content }) {
  return (
    <div className={`d-flex ${role === 'user' ? 'justify-content-end' : 'justify-content-start'}`}>
      <div className={`message-bubble ${role}`}>
        <ReactMarkdown
          remarkPlugins={[remarkGfm]}
          rehypePlugins={[rehypeRaw]}
          components={{
            img: ({ node, ...props }) => (
              <img {...props} className="img-fluid" alt={props.alt || ''} />
            ),
          }}
        >
          {content}
        </ReactMarkdown>
      </div>
    </div>
  );
}
