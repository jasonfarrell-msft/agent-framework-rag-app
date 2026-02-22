import { useState, useRef, useEffect } from 'react';
import ChatMessage from './components/ChatMessage';
import { sendMessage } from './services/api';
import './App.css';

function App() {
  const [messages, setMessages] = useState([]);
  const [input, setInput] = useState('');
  const [conversationId, setConversationId] = useState(null);
  const [loading, setLoading] = useState(false);
  const messagesContainerRef = useRef(null);
  const inputRef = useRef(null);

  const scrollToBottom = () => {
    const el = messagesContainerRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  };

  useEffect(() => {
    scrollToBottom();
  }, [messages, loading]);

  const handleSubmit = async (e) => {
    e.preventDefault();
    const trimmed = input.trim();
    if (!trimmed || loading) return;

    setMessages((prev) => [...prev, { role: 'user', content: trimmed }]);
    setInput('');
    setLoading(true);

    try {
      const data = await sendMessage(trimmed, conversationId);
      setConversationId(data.conversationId);
      setMessages((prev) => [...prev, { role: 'assistant', content: data.message }]);
    } catch (err) {
      console.error('Chat error:', err);
      setMessages((prev) => [...prev, { role: 'assistant', content: 'Sorry, something went wrong. Please try again.' }]);
    } finally {
      setLoading(false);
      inputRef.current?.focus();
    }
  };

  return (
    <div className="chat-wrapper">
      <div className="chat-container">
        <div className="chat-header d-flex align-items-center">
          <span>Chat</span>
        </div>

        <div className="chat-messages" ref={messagesContainerRef}>
          {messages.length === 0 && (
            <div className="text-center mt-5" style={{ color: '#6c757d' }}>
              <h5>Send a message to start a conversation</h5>
            </div>
          )}
          {messages.map((msg, idx) => (
            <ChatMessage key={idx} role={msg.role} content={msg.content} />
          ))}
          {loading && (
            <div className="d-flex justify-content-start">
              <div className="typing-indicator">
                <span></span>
                <span></span>
                <span></span>
              </div>
            </div>
          )}
        </div>

        <div className="chat-input-area">
          <form onSubmit={handleSubmit} className="d-flex gap-2">
            <input
              ref={inputRef}
              type="text"
              className="form-control"
              placeholder="Type a message…"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              disabled={loading}
              autoFocus
            />
            <button
              type="submit"
              className="btn btn-primary"
              disabled={loading || !input.trim()}
            >
              Send
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}

export default App;
