class IptvApp {
    constructor() {
        this.portals = [];
        this.currentChannels = [];
        this.currentPortalId = null;
        this.currentCategories = [];
        this.currentFilteredChannels = [];
        this.apiBaseUrl = window.location.origin;

        // Auth state
        this.isAuthenticated = false;
        this.currentUser = null;

        this.init();
    }

    async init() {
        this.setupEventListeners();
        this.setupAuthTabs();
        this.setupTabs();
        this.setupVideoPlayer();
        this.updateApiUrl();

        // Check authentication status
        await this.checkAuth();
    }

    // ========== Authentication Methods ==========

    async checkAuth() {
        try {
            const response = await fetch(`${this.apiBaseUrl}/api/auth/me`, {
                credentials: 'include'
            });

            if (response.ok) {
                const data = await response.json();
                if (data.success) {
                    this.isAuthenticated = true;
                    this.currentUser = data.data;
                    this.showMainContent();
                    await this.loadPortals();
                } else {
                    this.showAuthContainer();
                }
            } else {
                this.showAuthContainer();
            }
        } catch (error) {
            console.error('Error checking auth:', error);
            this.showAuthContainer();
        }
    }

    async login(e) {
        e.preventDefault();

        const username = document.getElementById('login-username').value.trim();
        const password = document.getElementById('login-password').value;

        try {
            const response = await fetch(`${this.apiBaseUrl}/api/auth/login`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                credentials: 'include',
                body: JSON.stringify({ username, password })
            });

            const data = await response.json();

            if (data.success) {
                this.isAuthenticated = true;
                this.currentUser = data.data;
                this.showToast('Login successful', 'success');
                document.getElementById('login-form').reset();
                this.showMainContent();
                await this.loadPortals();
            } else {
                this.showToast(data.error || 'Login failed', 'error');
            }
        } catch (error) {
            console.error('Error logging in:', error);
            this.showToast('Error connecting to server', 'error');
        }
    }

    async register(e) {
        e.preventDefault();

        const username = document.getElementById('register-username').value.trim();
        const email = document.getElementById('register-email').value.trim();
        const password = document.getElementById('register-password').value;

        try {
            const response = await fetch(`${this.apiBaseUrl}/api/auth/register`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                credentials: 'include',
                body: JSON.stringify({ username, email, password })
            });

            const data = await response.json();

            if (data.success) {
                this.isAuthenticated = true;
                this.currentUser = data.data;
                this.showToast('Registration successful', 'success');
                document.getElementById('register-form').reset();
                this.showMainContent();
                await this.loadPortals();
            } else {
                this.showToast(data.error || 'Registration failed', 'error');
            }
        } catch (error) {
            console.error('Error registering:', error);
            this.showToast('Error connecting to server', 'error');
        }
    }

    async logout() {
        try {
            const response = await fetch(`${this.apiBaseUrl}/api/auth/logout`, {
                method: 'POST',
                credentials: 'include'
            });

            const data = await response.json();

            if (data.success) {
                this.isAuthenticated = false;
                this.currentUser = null;
                this.portals = [];
                this.currentChannels = [];
                this.showToast('Logged out successfully', 'success');
                this.showAuthContainer();
            } else {
                this.showToast(data.error || 'Logout failed', 'error');
            }
        } catch (error) {
            console.error('Error logging out:', error);
            this.showToast('Error during logout', 'error');
        }
    }

    async changePassword(e) {
        e.preventDefault();

        const currentPassword = document.getElementById('current-password').value;
        const newPassword = document.getElementById('new-password').value;

        try {
            const response = await fetch(`${this.apiBaseUrl}/api/auth/change-password`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                credentials: 'include',
                body: JSON.stringify({ currentPassword, newPassword })
            });

            const data = await response.json();

            if (data.success) {
                this.showToast('Password changed successfully', 'success');
                document.getElementById('change-password-form').reset();
            } else {
                this.showToast(data.error || 'Failed to change password', 'error');
            }
        } catch (error) {
            console.error('Error changing password:', error);
            this.showToast('Error connecting to server', 'error');
        }
    }

    showAuthContainer() {
        document.getElementById('auth-container').style.display = 'flex';
        document.getElementById('main-content').classList.add('hidden');
        this.updateAuthSection();
    }

    showMainContent() {
        document.getElementById('auth-container').style.display = 'none';
        document.getElementById('main-content').classList.remove('hidden');
        this.updateAuthSection();
    }

    updateAuthSection() {
        const authSection = document.getElementById('auth-section');
        if (this.isAuthenticated && this.currentUser) {
            authSection.innerHTML = `
                <div class="user-info">
                    <span>Welcome, <strong>${this.escapeHtml(this.currentUser.username)}</strong></span>
                    <button class="btn btn-sm btn-secondary" onclick="app.logout()">Logout</button>
                </div>
            `;
        } else {
            authSection.innerHTML = '';
        }
    }

    // ========== UI Setup Methods ==========

    setupEventListeners() {
        // Portal form
        const portalForm = document.getElementById('add-portal-form');
        if (portalForm) {
            portalForm.addEventListener('submit', this.addPortal.bind(this));
        }

        // Portal selection
        const portalSelect = document.getElementById('channel-portal-select');
        if (portalSelect) {
            portalSelect.addEventListener('change', this.loadChannels.bind(this));
        }

        // Search functionality
        const searchInput = document.getElementById('channel-search');
        if (searchInput) {
            searchInput.addEventListener('input', this.searchChannels.bind(this));
        }

        // Modal
        const closeBtn = document.querySelector('.close');
        if (closeBtn) {
            closeBtn.addEventListener('click', this.closeModal.bind(this));
        }
        window.addEventListener('click', (e) => {
            if (e.target.classList.contains('modal')) {
                this.closeModal();
            }
        });

        // Auth forms
        const loginForm = document.getElementById('login-form');
        if (loginForm) {
            loginForm.addEventListener('submit', this.login.bind(this));
        }

        const registerForm = document.getElementById('register-form');
        if (registerForm) {
            registerForm.addEventListener('submit', this.register.bind(this));
        }

        const changePasswordForm = document.getElementById('change-password-form');
        if (changePasswordForm) {
            changePasswordForm.addEventListener('submit', this.changePassword.bind(this));
        }
    }

    setupAuthTabs() {
        const authTabs = document.querySelectorAll('.auth-tab');
        const authForms = document.querySelectorAll('.auth-form-container');

        authTabs.forEach(tab => {
            tab.addEventListener('click', () => {
                const tabName = tab.getAttribute('data-auth-tab');

                // Remove active class from all tabs and forms
                authTabs.forEach(t => t.classList.remove('active'));
                authForms.forEach(f => f.classList.remove('active'));

                // Add active class to clicked tab and corresponding form
                tab.classList.add('active');
                document.getElementById(`${tabName}-form-container`).classList.add('active');
            });
        });
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
        // Create video player container if it doesn't exist
        if (!document.getElementById('video-container')) {
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
    }

    updateApiUrl() {
        const apiUrlElement = document.getElementById('api-base-url');
        if (apiUrlElement) {
            apiUrlElement.textContent = this.apiBaseUrl;
        }
    }

    // ========== Portal Methods ==========

    async loadPortals() {
        if (!this.isAuthenticated) return;

        try {
            const response = await fetch(`${this.apiBaseUrl}/api/portals`, {
                credentials: 'include'
            });
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
        if (!container) return;

        if (this.portals.length === 0) {
            container.innerHTML = '<div class="loading">No portals added yet</div>';
            return;
        }

        // Clear container and build DOM elements properly to avoid HTML injection issues
        container.innerHTML = '';

        this.portals.forEach(portal => {
            const card = document.createElement('div');
            card.className = 'portal-card';

            const lastConnectedHtml = portal.lastConnected
                ? `<small>Last connected: ${new Date(portal.lastConnected).toLocaleDateString()}</small>`
                : '';

            card.innerHTML = `
                <div class="portal-name">${this.escapeHtml(portal.name)}</div>
                <div class="portal-url">${this.escapeHtml(portal.portalUrl)}</div>
                <div class="portal-mac">MAC: ${this.escapeHtml(portal.macAddress)}</div>
                <div class="portal-status ${portal.isActive ? 'online' : 'offline'}">
                    ${portal.isActive ? 'Active' : 'Inactive'}
                </div>
                ${lastConnectedHtml}
                <div class="portal-actions"></div>
            `;

            const actionsContainer = card.querySelector('.portal-actions');

            // Create buttons with proper event listeners instead of inline onclick
            const testBtn = document.createElement('button');
            testBtn.className = 'btn btn-sm btn-primary';
            testBtn.textContent = 'Test';
            testBtn.addEventListener('click', () => this.testPortal(portal.id));
            actionsContainer.appendChild(testBtn);

            const loadBtn = document.createElement('button');
            loadBtn.className = 'btn btn-sm btn-success';
            loadBtn.textContent = 'Load Channels';
            loadBtn.addEventListener('click', () => this.loadPortalChannels(portal.id));
            actionsContainer.appendChild(loadBtn);

            const deleteBtn = document.createElement('button');
            deleteBtn.className = 'btn btn-sm btn-danger';
            deleteBtn.textContent = 'Delete';
            deleteBtn.addEventListener('click', () => this.deletePortal(portal.id));
            actionsContainer.appendChild(deleteBtn);

            container.appendChild(card);
        });
    }

    updatePortalSelect() {
        const select = document.getElementById('channel-portal-select');
        if (!select) return;

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
                credentials: 'include',
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
                method: 'POST',
                credentials: 'include'
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
        const channelsTab = document.querySelector('.tab[data-tab="channels"]');
        if (channelsTab) {
            channelsTab.click();
        }
        const portalSelect = document.getElementById('channel-portal-select');
        if (portalSelect) {
            portalSelect.value = portalId;
            await this.loadChannels();
        }
    }

    async deletePortal(portalId) {
        if (!confirm('Are you sure you want to delete this portal?')) {
            return;
        }

        try {
            const response = await fetch(`${this.apiBaseUrl}/api/portals/${portalId}`, {
                method: 'DELETE',
                credentials: 'include'
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

    // ========== Channel Methods ==========

    async loadChannels() {
        const portalId = document.getElementById('channel-portal-select').value;

        if (!portalId) {
            const channelsList = document.getElementById('channels-list');
            if (channelsList) {
                channelsList.innerHTML = '<div class="loading">Select a portal to view channels</div>';
            }
            return;
        }

        this.currentPortalId = portalId;

        try {
            const channelsList = document.getElementById('channels-list');
            if (channelsList) {
                channelsList.innerHTML = '<div class="loading">Loading channels...</div>';
            }

            const response = await fetch(`${this.apiBaseUrl}/api/channels/portal/${portalId}`, {
                credentials: 'include'
            });
            const data = await response.json();

            if (data.success) {
                this.currentChannels = data.data.channels;
                this.currentFilteredChannels = this.currentChannels;
                this.renderChannels();
                await this.loadCategories(portalId);
            } else {
                this.showToast(data.error || 'Failed to load channels', 'error');
                if (channelsList) {
                    channelsList.innerHTML = '<div class="loading">Failed to load channels</div>';
                }
            }
        } catch (error) {
            console.error('Error loading channels:', error);
            this.showToast('Error loading channels', 'error');
            const channelsList = document.getElementById('channels-list');
            if (channelsList) {
                channelsList.innerHTML = '<div class="loading">Error loading channels</div>';
            }
        }
    }

    async loadCategories(portalId) {
        try {
            const response = await fetch(`${this.apiBaseUrl}/api/channels/portal/${portalId}/categories`, {
                credentials: 'include'
            });
            const data = await response.json();

            if (data.success && data.data && data.data.categories) {
                this.currentCategories = [{ id: 'all', title: 'All' }, ...data.data.categories];
                this.renderCategories();
            }
        } catch (error) {
            console.error('Error loading categories:', error);
        }
    }

    renderCategories() {
        const container = document.querySelector('.category-filter');
        if (!container) return;

        container.innerHTML = '';

        this.currentCategories.forEach(category => {
            const btn = document.createElement('button');
            const isAll = category.id === 'all';
            const categoryId = isAll ? 'all' : category.id;
            const categoryTitle = isAll ? 'All' : category.title;

            btn.className = `category-btn ${isAll ? 'active' : ''}`;
            btn.textContent = categoryTitle;
            btn.dataset.categoryId = categoryId;
            btn.addEventListener('click', () => {
                this.selectCategory(categoryId);

                // Update active state
                container.querySelectorAll('.category-btn').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
            });
            container.appendChild(btn);
        });
    }

    async selectCategory(categoryId) {
        if (categoryId === 'all') {
            await this.loadChannels();
        } else {
            await this.loadChannelsByCategory(categoryId);
        }
    }

    async loadChannelsByCategory(categoryId) {
        const portalId = document.getElementById('channel-portal-select').value;

        if (!portalId) {
            return;
        }

        this.currentPortalId = portalId;

        try {
            const channelsList = document.getElementById('channels-list');
            if (channelsList) {
                channelsList.innerHTML = '<div class="loading">Loading channels...</div>';
            }

            const response = await fetch(`${this.apiBaseUrl}/api/channels/portal/${portalId}?categoryId=${encodeURIComponent(categoryId)}`, {
                credentials: 'include'
            });
            const data = await response.json();

            if (data.success) {
                this.currentFilteredChannels = data.data.channels;
                this.renderChannels();
            } else {
                this.showToast(data.error || 'Failed to load channels', 'error');
                if (channelsList) {
                    channelsList.innerHTML = '<div class="loading">Failed to load channels</div>';
                }
            }
        } catch (error) {
            console.error('Error loading channels by category:', error);
            this.showToast('Error loading channels', 'error');
            const channelsList = document.getElementById('channels-list');
            if (channelsList) {
                channelsList.innerHTML = '<div class="loading">Error loading channels</div>';
            }
        }
    }

    renderChannels() {
        const container = document.getElementById('channels-list');
        if (!container) return;

        if (this.currentFilteredChannels.length === 0) {
            container.innerHTML = '<div class="loading">No channels found</div>';
            return;
        }

        container.innerHTML = '';

        this.currentFilteredChannels.forEach(channel => {
            const card = document.createElement('div');
            card.className = 'channel-card';
            card.innerHTML = `
                <div class="channel-number">${channel.number}</div>
                <div class="channel-name" title="${this.escapeHtml(channel.name)}">${this.escapeHtml(channel.name)}</div>
            `;
            card.addEventListener('click', () => this.playChannel(channel.id, this.currentPortalId));
            container.appendChild(card);
        });
    }

    searchChannels() {
        const query = document.getElementById('channel-search').value.toLowerCase();

        this.currentFilteredChannels = this.currentChannels.filter(channel =>
            channel.name.toLowerCase().includes(query)
        );

        this.renderChannels();
    }

    // ========== Video Player Methods ==========

    playChannel(channelId, portalId) {
        const streamUrl = `${this.apiBaseUrl}/api/proxy/stream?portalId=${portalId}&channelId=${channelId}`;

        const player = document.getElementById('video-player');
        const container = document.getElementById('video-container');

        if (!player || !container) return;

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

        if (!player || !container) return;

        player.pause();
        player.src = '';
        container.style.display = 'none';
    }

    // ========== Utility Methods ==========

    showModal(title, content) {
        const modalTitle = document.getElementById('modal-title');
        const modalBody = document.getElementById('modal-body');
        const modal = document.getElementById('portal-modal');

        if (modalTitle) modalTitle.textContent = title;
        if (modalBody) modalBody.innerHTML = content;
        if (modal) modal.style.display = 'block';
    }

    closeModal() {
        const modal = document.getElementById('portal-modal');
        if (modal) modal.style.display = 'none';
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
        if (!text) return '';
        const map = {
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#039;'
        };
        return text.replace(/[&<>"']/g, m => map[m]);
    }
}

// Initialize app when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    window.app = new IptvApp();
});
