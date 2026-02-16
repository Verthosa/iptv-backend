class IptvApp {
    constructor() {
        this.portals = [];
        this.currentChannels = [];
        this.currentPortalId = null;
        this.currentCategories = [];
        this.currentFilteredChannels = [];
        this.apiBaseUrl = window.location.origin;
        
        this.init();
    }

    async init() {
        this.setupEventListeners();
        this.setupTabs();
        this.loadPortals();
        this.updateApiUrl();
    }

    setupEventListeners() {
        // Portal form
        document.getElementById('add-portal-form').addEventListener('submit', this.addPortal.bind(this));
        
        // Portal selection
        document.getElementById('channel-portal-select').addEventListener('change', this.loadChannels.bind(this));
        
        // Search functionality
        document.getElementById('channel-search').addEventListener('input', this.searchChannels.bind(this));
        
        // Modal
        document.querySelector('.close').addEventListener('click', this.closeModal.bind(this));
        window.addEventListener('click', (e) => {
            if (e.target.classList.contains('modal')) {
                this.closeModal();
            }
        });
        
        // Video player
        this.setupVideoPlayer();
    }

    setupTabs() {
        const tabs = document.querySelectorAll('.tab');
        const tabContents = document.querySelectorAll('.tab-content');

        tabs.forEach(tab => {
            tab.addEventListener('click', () => {
                const tabName = tab.getAttribute('data-tab');
                
                // Remove active class from all tabs and contents
                tabs.forEach(t => t.classList.remove('active'));
                tabContents.forEach(tc => tc.classList.remove('active'));
                
                // Add active class to clicked tab and corresponding content
                tab.classList.add('active');
                document.getElementById(`${tabName}-tab`).classList.add('active');
            });
        });
    }

    setupVideoPlayer() {
        // Create video player container
        const videoContainer = document.createElement('div');
        videoContainer.id = 'video-container';
        videoContainer.innerHTML = `
            <video id="video-player" controls></video>
            <button id="close-player">Close Player</button>
        `;
        document.body.appendChild(videoContainer);
        
        // Close button
        document.getElementById('close-player').addEventListener('click', this.closeVideoPlayer.bind(this));
    }

    updateApiUrl() {
        document.getElementById('api-base-url').textContent = this.apiBaseUrl;
    }

    async loadPortals() {
        try {
            const response = await fetch(`${this.apiBaseUrl}/api/portals`);
            const data = await response.json();
            
            if (data.success) {
                this.portals = data.data;
                this.renderPortals();
                this.updatePortalSelect();
            } else {
                this.showToast('Failed to load portals', 'error');
            }
        } catch (error) {
            console.error('Error loading portals:', error);
            this.showToast('Error connecting to server', 'error');
        }
    }

    renderPortals() {
        const container = document.getElementById('portals-list');
        
        if (this.portals.length === 0) {
            container.innerHTML = '<div class="loading">No portals added yet</div>';
            return;
        }

        container.innerHTML = this.portals.map(portal => `
            <div class="portal-card">
                <div class="portal-name">${this.escapeHtml(portal.name)}</div>
                <div class="portal-url">${this.escapeHtml(portal.portalUrl)}</div>
                <div class="portal-mac">MAC: ${this.escapeHtml(portal.macAddress)}</div>
                <div class="portal-status ${portal.isActive ? 'online' : 'offline'}">
                    ${portal.isActive ? 'Active' : 'Inactive'}
                </div>
                ${portal.lastConnected ? `
                    <small>Last connected: ${new Date(portal.lastConnected).toLocaleDateString()}</small>
                ` : ''}
                <div class="portal-actions">
                    <button class="btn btn-sm btn-primary" onclick="app.testPortal('${portal.id}')">
                        Test
                    </button>
                    <button class="btn btn-sm btn-success" onclick="app.loadPortalChannels('${portal.id}')">
                        Load Channels
                    </button>
                    <button class="btn btn-sm btn-danger" onclick="app.deletePortal('${portal.id}')">
                        Delete
                    </button>
                </div>
            </div>
        `).join('');
    }

    updatePortalSelect() {
        const select = document.getElementById('channel-portal-select');
        select.innerHTML = '<option value="">-- Select a Portal --</option>' +
            this.portals.filter(p => p.isActive).map(portal => `
                <option value="${portal.id}">${this.escapeHtml(portal.name)}</option>
            `).join('');
    }

    async addPortal(e) {
        e.preventDefault();
        
        const form = e.target;
        const name = document.getElementById('portal-name').value;
        const portalUrl = document.getElementById('portal-url').value;
        const macAddress = document.getElementById('portal-mac').value.toUpperCase();

        try {
            const response = await fetch(`${this.apiBaseUrl}/api/portals`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ name, portalUrl, macAddress })
            });

            const data = await response.json();

            if (data.success) {
                this.showToast('Portal added successfully', 'success');
                form.reset();
                await this.loadPortals();
            } else {
                this.showToast(data.error || 'Failed to add portal', 'error');
            }
        } catch (error) {
            console.error('Error adding portal:', error);
            this.showToast('Error adding portal', 'error');
        }
    }

    async testPortal(portalId) {
        try {
            this.showToast('Testing connection...', 'success');
            
            const response = await fetch(`${this.apiBaseUrl}/api/portals/${portalId}/test`, {
                method: 'POST'
            });
            
            const data = await response.json();
            
            if (data.success) {
                this.showToast(data.message, 'success');
                await this.loadPortals();
            } else {
                this.showToast(data.error || 'Connection test failed', 'error');
            }
        } catch (error) {
            console.error('Error testing portal:', error);
            this.showToast('Error testing connection', 'error');
        }
    }

    async loadPortalChannels(portalId) {
        // Switch to channels tab
        document.querySelector('.tab[data-tab="channels"]').click();
        document.getElementById('channel-portal-select').value = portalId;
        await this.loadChannels();
    }

    async loadChannels() {
        const portalId = document.getElementById('channel-portal-select').value;
        
        if (!portalId) {
            document.getElementById('channels-list').innerHTML = '<div class="loading">Select a portal to view channels</div>';
            return;
        }

        this.currentPortalId = portalId;
        
        try {
            document.getElementById('channels-list').innerHTML = '<div class="loading">Loading channels...</div>';
            
            const response = await fetch(`${this.apiBaseUrl}/api/channels/portal/${portalId}`);
            const data = await response.json();
            
            if (data.success) {
                this.currentChannels = data.data.channels;
                this.currentFilteredChannels = this.currentChannels;
                this.renderChannels();
                await this.loadCategories(portalId);
            } else {
                this.showToast(data.error || 'Failed to load channels', 'error');
                document.getElementById('channels-list').innerHTML = '<div class="loading">Failed to load channels</div>';
            }
        } catch (error) {
            console.error('Error loading channels:', error);
            this.showToast('Error loading channels', 'error');
            document.getElementById('channels-list').innerHTML = '<div class="loading">Error loading channels</div>';
        }
    }

    async loadCategories(portalId) {
        try {
            const response = await fetch(`${this.apiBaseUrl}/api/channels/portal/${portalId}/categories`);
            const data = await response.json();
            
            if (data.success) {
                this.currentCategories = ['all', ...data.data];
                this.renderCategories();
            }
        } catch (error) {
            console.error('Error loading categories:', error);
        }
    }

    renderCategories() {
        const container = document.querySelector('.category-filter');
        container.innerHTML = this.currentCategories.map(category => `
            <button class="category-btn ${category === 'all' ? 'active' : ''}" 
                    data-category="${category}">
                ${category === 'all' ? 'All' : category}
            </button>
        `).join('');

        // Add click events to category buttons
        container.querySelectorAll('.category-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const category = btn.getAttribute('data-category');
                this.filterByCategory(category);
                
                // Update active state
                container.querySelectorAll('.category-btn').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
            });
        });
    }

    renderChannels() {
        const container = document.getElementById('channels-list');
        
        if (this.currentFilteredChannels.length === 0) {
            container.innerHTML = '<div class="loading">No channels found</div>';
            return;
        }

        container.innerHTML = this.currentFilteredChannels.map(channel => `
            <div class="channel-card" onclick="app.playChannel('${channel.id}', '${this.currentPortalId}')">
                <div class="channel-number">${channel.number}</div>
                <div class="channel-name" title="${this.escapeHtml(channel.name)}">${this.escapeHtml(channel.name)}</div>
            </div>
        `).join('');
    }

    searchChannels() {
        const query = document.getElementById('channel-search').value.toLowerCase();
        
        this.currentFilteredChannels = this.currentChannels.filter(channel => 
            channel.name.toLowerCase().includes(query)
        );
        
        this.renderChannels();
    }

    filterByCategory(category) {
        if (category === 'all') {
            this.currentFilteredChannels = this.currentChannels;
        } else {
            this.currentFilteredChannels = this.currentChannels.filter(channel => 
                channel.category === category
            );
        }
        
        this.renderChannels();
    }

    playChannel(channelId, portalId) {
        const streamUrl = `${this.apiBaseUrl}/api/proxy/stream?portalId=${portalId}&channelId=${channelId}`;
        
        const player = document.getElementById('video-player');
        const container = document.getElementById('video-container');
        
        player.src = streamUrl;
        container.style.display = 'block';
        
        player.play().catch(e => {
            this.showToast('Failed to play stream', 'error');
            console.error('Playback error:', e);
        });
    }

    closeVideoPlayer() {
        const player = document.getElementById('video-player');
        const container = document.getElementById('video-container');
        
        player.pause();
        player.src = '';
        container.style.display = 'none';
    }

    async deletePortal(portalId) {
        if (!confirm('Are you sure you want to delete this portal?')) {
            return;
        }

        try {
            const response = await fetch(`${this.apiBaseUrl}/api/portals/${portalId}`, {
                method: 'DELETE'
            });

            const data = await response.json();

            if (data.success) {
                this.showToast('Portal deleted', 'success');
                await this.loadPortals();
            } else {
                this.showToast(data.error || 'Failed to delete portal', 'error');
            }
        } catch (error) {
            console.error('Error deleting portal:', error);
            this.showToast('Error deleting portal', 'error');
        }
    }

    showModal(title, content) {
        document.getElementById('modal-title').textContent = title;
        document.getElementById('modal-body').innerHTML = content;
        document.getElementById('portal-modal').style.display = 'block';
    }

    closeModal() {
        document.getElementById('portal-modal').style.display = 'none';
    }

    showToast(message, type = 'success') {
        const toast = document.createElement('div');
        toast.className = `toast ${type}`;
        toast.textContent = message;
        
        document.body.appendChild(toast);
        
        setTimeout(() => {
            toast.remove();
        }, 3000);
    }

    escapeHtml(text) {
        const map = {
            '&': '&',
            '<': '<',
            '>': '>',
            '"': '"',
            "'": '''
        };
        return text.replace(/[&<>"']/g, m => map[m]);
    }
}

// Initialize app when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    window.app = new IptvApp();
});
