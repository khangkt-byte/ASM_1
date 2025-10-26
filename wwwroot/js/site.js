// Sticky Navigation & Scroll Effects
const PERSISTENT_NOTIFICATION_KEY = 'site:persistentNotifications';

function loadPersistentNotifications() {
    try {
        const raw = sessionStorage.getItem(PERSISTENT_NOTIFICATION_KEY);
        if (!raw) {
            return [];
        }

        const parsed = JSON.parse(raw);
        if (!Array.isArray(parsed)) {
            return [];
        }

        const sanitized = parsed
            .map(item => {
                if (!item || typeof item !== 'object') {
                    return null;
                }

                const id = typeof item.id === 'string' && item.id ? item.id : null;
                const message = typeof item.message === 'string' ? item.message : '';
                if (!id || !message) {
                    return null;
                }

                const type = typeof item.type === 'string' && item.type ? item.type : 'info';
                const duration = Number.isFinite(item.duration) && item.duration > 0
                    ? item.duration
                    : 3000;

                return {
                    id,
                    message,
                    type,
                    duration
                };
            })
            .filter(Boolean);

        return sanitized;
    } catch {
        return [];
    }
}

function savePersistentNotifications(entries) {
    try {
        if (!Array.isArray(entries) || !entries.length) {
            sessionStorage.removeItem(PERSISTENT_NOTIFICATION_KEY);
            return;
        }

        sessionStorage.setItem(PERSISTENT_NOTIFICATION_KEY, JSON.stringify(entries));
    } catch {
        // ignore storage errors (e.g. private mode)
    }
}

function persistNotificationEntry(entry) {
    const queue = loadPersistentNotifications();
    const nextQueue = queue.filter(item => item.id !== entry.id);
    nextQueue.push(entry);
    savePersistentNotifications(nextQueue);
}

function removePersistentNotification(id) {
    if (!id) {
        return;
    }

    const queue = loadPersistentNotifications();
    const nextQueue = queue.filter(item => item.id !== id);
    savePersistentNotifications(nextQueue);
}

document.addEventListener('DOMContentLoaded', function () {
    const header = document.querySelector('.header-nav');
    const mobileToggle = document.querySelector('.mobile-menu-toggle');
    const navMenu = document.querySelector('.nav-menu');

    if (header) {
        // Sticky navigation với hiệu ứng
        window.addEventListener('scroll', function () {
            if (window.scrollY > 100) {
                header.classList.add('scrolled');
            } else {
                header.classList.remove('scrolled');
            }
        });
    }

    // Mobile menu toggle
    if (mobileToggle && navMenu) {
        mobileToggle.addEventListener('click', function () {
            navMenu.classList.toggle('mobile-active');
            this.classList.toggle('active');
        });
    }

    // Smooth scroll cho internal links
    document.querySelectorAll('a[href^="#"]').forEach(anchor => {
        anchor.addEventListener('click', function (e) {
            e.preventDefault();
            const target = document.querySelector(this.getAttribute('href'));
            if (target) {
                target.scrollIntoView({
                    behavior: 'smooth',
                    block: 'start'
                });
            }
        });
    });

    // Animate elements on scroll
    const observerOptions = {
        threshold: 0.1,
        rootMargin: '0px 0px -50px 0px'
    };

    const observer = new IntersectionObserver(function (entries) {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.classList.add('fade-in-up');
            }
        });
    }, observerOptions);

    // Observe elements for animation
    document.querySelectorAll('.menu-item-card, .footer-section').forEach(el => {
        observer.observe(el);
    });

    const queuedNotifications = loadPersistentNotifications();
    if (queuedNotifications.length) {
        // ensure sanitized queue is written back so invalid entries are cleared
        savePersistentNotifications(queuedNotifications);
        queuedNotifications.forEach(entry => {
            showNotification(entry.message, entry.type, {
                duration: entry.duration,
                storageId: entry.id
            });
        });
    }
});

function addToCart(foodId) {
    // Hiệu ứng thêm vào giỏ hàng
    const cartBtn = document.querySelector('.cart-btn');
    if (cartBtn) {
        cartBtn.style.transform = 'scale(1.1)';
        cartBtn.style.background = 'var(--success-color)';

        setTimeout(() => {
            cartBtn.style.transform = '';
            cartBtn.style.background = '';
        }, 300);
    }

    showNotification('Đã thêm món vào giỏ hàng!', 'success');
}

function showNotification(message, type = 'info', options) {
    const opts = (options && typeof options === 'object') ? options : {};
    const duration = Number.isFinite(opts.duration) && opts.duration > 0 ? opts.duration : 3000;
    const persist = Boolean(opts.persist);
    const storageId = typeof opts.storageId === 'string' && opts.storageId ? opts.storageId : null;

    const id = storageId || `notif-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;

    if (persist && !storageId) {
        persistNotificationEntry({
            id,
            message,
            type,
            duration
        });
    }

    const notification = document.createElement('div');
    notification.className = `notification ${type}`;
    notification.dataset.notificationId = id;
    notification.setAttribute('role', 'status');

    const background = {
        success: 'var(--success-color)',
        warning: 'var(--warning-color)',
        error: 'var(--danger-color)',
        danger: 'var(--danger-color)'
    }[type] || 'var(--primary-color)';

    const iconClass = {
        success: 'fa-check-circle',
        warning: 'fa-triangle-exclamation',
        error: 'fa-circle-xmark',
        danger: 'fa-circle-xmark'
    }[type] || 'fa-circle-info';

    notification.style.cssText = `
        position: fixed;
        top: 20px;
        right: 20px;
        padding: 16px 20px;
        background: ${background};
        color: white;
        border-radius: var(--border-radius);
        z-index: 10000;
        display: flex;
        align-items: center;
        gap: 8px;
        box-shadow: var(--shadow-hover);
        animation: slideInRight 0.3s ease;
        font-weight: 500;
        cursor: pointer;
    `;

    const icon = document.createElement('i');
    icon.className = `fas ${iconClass}`;
    const messageSpan = document.createElement('span');
    messageSpan.textContent = message;

    notification.appendChild(icon);
    notification.appendChild(messageSpan);

    document.body.appendChild(notification);

    let dismissed = false;
    const finalizeRemoval = () => {
        if (dismissed) {
            return;
        }
        dismissed = true;
        if (persist || storageId) {
            removePersistentNotification(id);
        }
        notification.remove();
    };

    const hideNotification = () => {
        if (dismissed) {
            return;
        }
        dismissed = true;
        notification.style.animation = 'slideOutRight 0.3s ease';
        setTimeout(() => {
            if (persist || storageId) {
                removePersistentNotification(id);
            }
            notification.remove();
        }, 300);
    };

    let hideTimer = window.setTimeout(hideNotification, duration);

    notification.addEventListener('click', () => {
        window.clearTimeout(hideTimer);
        hideNotification();
    });

    notification.addEventListener('mouseenter', () => {
        window.clearTimeout(hideTimer);
    });

    notification.addEventListener('mouseleave', () => {
        if (!dismissed) {
            window.clearTimeout(hideTimer);
            hideTimer = window.setTimeout(hideNotification, 500);
        }
    });

    return {
        id,
        element: notification,
        remove: finalizeRemoval
    };
}

function updateCartCount(addQuantity = 1) {
    const cartCount = document.querySelector('.cart-count');
    if (cartCount) {
        let currentCount = parseInt(cartCount.textContent) || 0;
        cartCount.textContent = currentCount + addQuantity;

        // Animation effect
        cartCount.style.animation = 'bounce 0.5s ease';
        setTimeout(() => {
            cartCount.style.animation = '';
        }, 500);
    }
}

// Admin Panel JavaScript Functions

// Sidebar toggle
function toggleSidebar() {
    const sidebar = document.querySelector('.admin-sidebar');
    const main = document.querySelector('.admin-main');

    sidebar.classList.toggle('collapsed');
    main.classList.toggle('expanded');
}

// Mobile sidebar toggle
function toggleMobileSidebar() {
    const sidebar = document.querySelector('.admin-sidebar');
    sidebar.classList.toggle('show');
}

// Dashboard functions
document.addEventListener('DOMContentLoaded', function () {
    // Handle mobile menu
    const menuToggle = document.querySelector('.menu-toggle');
    if (menuToggle) {
        menuToggle.addEventListener('click', function () {
            if (window.innerWidth <= 768) {
                toggleMobileSidebar();
            } else {
                toggleSidebar();
            }
        });
    }

    // Set active nav item based on current URL
    const currentPath = window.location.pathname;
    const navItems = document.querySelectorAll('.nav-item');

    navItems.forEach(item => {
        if (item.getAttribute('href') === currentPath) {
            item.classList.add('active');
        }
    });

});

// Utility functions for admin
function confirmAction(message, callback) {
    if (confirm(message)) {
        callback();
    }
}

function showAdminNotification(message, type = 'info') {
    // Reuse the existing notification system
    showNotification(message, type);
}

// Export functions
window.adminFunctions = {
    toggleSidebar,
    toggleMobileSidebar,
    confirmAction,
    showAdminNotification
};
