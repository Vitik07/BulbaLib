const currentHost = window.location.hostname;
const baseUrl = `https://localhost:5000`;

const novelList = document.getElementById('novelList');
const searchInput = document.getElementById('searchInput');
const profileName = document.getElementById('profileName');
const profileAvatar = document.getElementById('profileAvatar');
const profileButton = document.getElementById('profileButton');
const loginButton = document.getElementById('loginButton');
const userId = localStorage.getItem('userId');
const devButton = document.getElementById('devInfoButton');
const devModal = document.getElementById('devModal');
const closeModal = document.getElementById('closeModal');;

let allNovels = [];

devButton.addEventListener('click', () => {
    devModal.style.display = 'block';
});

closeModal.addEventListener('click', () => {
    devModal.style.display = 'none';
});

window.addEventListener('click', (e) => {
    if (e.target === devModal) {
        devModal.style.display = 'none';
    }
});

async function loadAllNovels() {
    try {
        const response = await fetch(`${baseUrl}/novels`);
        allNovels = await response.json();
        console.log("📚 Все новеллы загружены:", allNovels);
        showNovels(allNovels);
    } catch (error) {
        console.error("❌ Не удалось загрузить список новелл", error);
        novelList.innerHTML = '<p>Ошибка загрузки данных</p>';
    }
}

function showNovels(novels) {
    novelList.innerHTML = '';

    if (!novels.length) {
        novelList.innerHTML = '<p>Новеллы не найдены</p>';
        return;
    }

    novels.forEach((novel, index) => {
        const card = document.createElement('div');
        card.className = 'novel-card';
        card.style.setProperty('--i', index);
        card.onclick = () => {
            location.href = `novel.html?id=${novel.id}`;
        };

        const imageUrl = novel.image
            ? `data:image/jpeg;base64,${novel.image}`
            : 'Resource/default.jpg';

        card.innerHTML = `
        <img src="${imageUrl}" alt="${novel.title}" onerror="this.onerror=null; this.src='Resource/default.jpg';">
        <div class="title-wrapper">
          <div class="title">${novel.title}</div>
          <div class="tooltip">${novel.title}</div>
        </div>
      `;
        novelList.appendChild(card);
    });
}

function filterNovels(term) {
    if (!term.trim()) {
        showNovels(allNovels);
        return;
    }

    const search = term.toLowerCase();
    const results = allNovels.filter(novel =>
        novel.title.toLowerCase().includes(search)
    );
    showNovels(results);
}

document.addEventListener('DOMContentLoaded', () => {
    loadAllNovels();

    searchInput.addEventListener('input', (e) => {
        filterNovels(e.target.value);
    });

    document.querySelector('.button').addEventListener('click', () => {
        if (allNovels.length === 0) {
            alert("Нет новелл");
            return;
        }
        const randomNovel = allNovels[Math.floor(Math.random() * allNovels.length)];
        window.location.href = `novel.html?id=${randomNovel.id}`;
    });

    console.log("userId из localStorage:", userId);
    console.log("Кнопка логина:", loginButton);
    if (!userId) {
        profileName.textContent = 'Гость';
        profileAvatar.src = 'Resource/default-avatar.jpg';

        // Делаем кнопку видимой и кликабельной
        loginButton.style.visibility = 'visible';
        loginButton.style.opacity = '1';
        loginButton.style.pointerEvents = 'auto';

        // Назначаем обработчик только один раз
        loginButton.onclick = () => {
            window.location.href = 'login.html';
        };
    } else {
        loginButton.style.visibility = 'hidden';
        loginButton.style.opacity = '0';
        loginButton.style.pointerEvents = 'none';
    }

    if (userId) {
        fetch(`${baseUrl}/users/${userId}`)
            .then(res => res.json())
            .then(user => {
                profileName.textContent = user.login;
                profileAvatar.src = `${baseUrl}/users/${userId}/avatar`;
            })
            .catch(err => {
                console.error("Ошибка загрузки профиля", err);
                profileName.textContent = 'Ошибка';
                profileAvatar.src = 'Resource/default-avatar.jpg';
            });
    }

    profileButton.addEventListener('click', () => {
        if (userId) {
            window.location.href = `profile.html?userId=${userId}`;
        } else {
            alert('Пользователь не авторизован');
            window.location.href = 'login.html';
        }
    });
});