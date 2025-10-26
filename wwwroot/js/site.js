// Sticky Navigation & Scroll Effects
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

function showNotification(message, type = 'info') {
    const notification = document.createElement('div');
    notification.className = `notification ${type}`;
    notification.innerHTML = `
        <i class="fas fa-check-circle"></i>
        <span>${message}</span>
    `;

    notification.style.cssText = `
        position: fixed;
        top: 20px;
        right: 20px;
        padding: 16px 20px;
        background: ${type === 'success' ? 'var(--success-color)' : 'var(--primary-color)'};
        color: white;
        border-radius: var(--border-radius);
        z-index: 10000;
        display: flex;
        align-items: center;
        gap: 8px;
        box-shadow: var(--shadow-hover);
        animation: slideInRight 0.3s ease;
        font-weight: 500;
    `;

    document.body.appendChild(notification);

    setTimeout(() => {
        notification.style.animation = 'slideOutRight 0.3s ease';
        setTimeout(() => notification.remove(), 300);
    }, 3000);
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
